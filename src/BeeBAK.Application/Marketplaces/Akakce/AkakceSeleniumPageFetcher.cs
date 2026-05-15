using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceSeleniumPageFetcher : ITransientDependency
{
    private readonly ILogger<AkakceSeleniumPageFetcher> _logger;

    // FlareSolverr listing fetch — sıralı (session tabanlı, Cloudflare bir kez çözülür).
    private static readonly System.Threading.SemaphoreSlim _flareSolverrPageSem = new(1, 1);
    // FlareSolverr detail fetch — en fazla 1 eş zamanlı (CPU baskısını azaltmak için)
    private static readonly System.Threading.SemaphoreSlim _flareSolverrDetailSem = new(1, 1);
    // FlareSolverr cookie inject için eski semaphore (fallback path)
    private static readonly System.Threading.SemaphoreSlim _flareSolverrSem = new(1, 1);
    private static string? _cachedCookieHeader;
    private static DateTime _cookieCachedAt = DateTime.MinValue;
    private static readonly TimeSpan _cookieTtl = TimeSpan.FromMinutes(20);

    public AkakceSeleniumPageFetcher(ILogger<AkakceSeleniumPageFetcher> logger)
    {
        _logger = logger;
    }

    public async Task<string?> TryGetListingHtmlAsync(
        string absoluteUrl,
        AkakceClientOptions options,
        CancellationToken cancellationToken = default)
    {
        // FlareSolverr yapılandırılmışsa sayfayı doğrudan FlareSolverr üzerinden çek.
        // cf_clearance FlareSolverr'ın kendi Chrome oturumuna bağlı olduğundan cookie inject
        // çalışmıyor; bunun yerine FlareSolverr'ın kendi tarayıcısıyla rendered HTML alıyoruz.
        if (!string.IsNullOrWhiteSpace(options.FlareSolverrUrl))
        {
            return await FetchListingViaFlareSolverrAsync(absoluteUrl, options, cancellationToken);
        }

        return await Task.Run(
            () => RunWithDriver(
                options,
                driver =>
                {
                    PrepareAndNavigate(driver, absoluteUrl, options);
                    WaitForCss(driver, "#PDL, ul[id=PDL], li[data-pr]", options.ListingWaitTimeoutMs, cancellationToken, throwOnTimeout: false);
                    DismissCookieBanner(driver);
                    ScrollPage(driver, options, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return driver.PageSource;
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// FlareSolverr'ın kendi Chrome oturumunu kullanarak listing sayfasını çeker.
    /// Aynı "akakce-listing" session ID'si ile Cloudflare sadece bir kez çözülür;
    /// sonraki sayfalar session cookie'lerini yeniden kullanarak ~5-15 s'de döner.
    /// Sıralı erişim (SemaphoreSlim(1,1)) FlareSolverr'ı paralel isteklerden korur.
    /// </summary>
    private async Task<string?> FetchListingViaFlareSolverrAsync(
        string absoluteUrl,
        AkakceClientOptions options,
        CancellationToken cancellationToken)
    {
        await _flareSolverrPageSem.WaitAsync(cancellationToken);
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(130) };

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                cmd = "request.get",
                url = absoluteUrl,
                maxTimeout = 90000,
                session = "akakce-listing"
            });

            _logger.LogInformation("Akakce FlareSolverr: sayfa alınıyor → {Url}", absoluteUrl);

            var resp = await http.PostAsync(
                options.FlareSolverrUrl!.TrimEnd('/') + "/v1",
                new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Akakce FlareSolverr sayfa HTTP {Status} → {Url}", (int)resp.StatusCode, absoluteUrl);
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = System.Text.Json.JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("solution", out var solution))
            {
                _logger.LogWarning("Akakce FlareSolverr: 'solution' alanı yok → {Url}", absoluteUrl);
                return null;
            }

            var httpStatus = solution.TryGetProperty("status", out var st) ? st.GetInt32() : 0;
            var html = solution.TryGetProperty("response", out var r) ? r.GetString() : null;

            _logger.LogInformation(
                "Akakce FlareSolverr: HTTP {HttpStatus}, {HtmlLen} karakter alındı → {Url}",
                httpStatus, html?.Length ?? 0, absoluteUrl);

            return html;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Akakce FlareSolverr sayfa hatası → {Url}", absoluteUrl);
            return null;
        }
        finally
        {
            _flareSolverrPageSem.Release();
        }
    }

    public async Task<string?> TryGetProductDetailHtmlAsync(
        string absoluteProductUrl,
        AkakceClientOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(options.FlareSolverrUrl))
        {
            return await FetchDetailViaFlareSolverrAsync(absoluteProductUrl, options, cancellationToken);
        }

        return await Task.Run(
            () => RunWithDriver(
                options,
                driver =>
                {
                    PrepareAndNavigate(driver, absoluteProductUrl, options);
                    WaitForCss(driver, "h1, li, tr", options.DetailWaitTimeoutMs, cancellationToken, throwOnTimeout: false);
                    DismissCookieBanner(driver);
                    ScrollPage(driver, options, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return driver.PageSource;
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Ürün detay sayfasını FlareSolverr üzerinden çeker.
    /// "akakce-detail" session'ı listing'ten bağımsız; en fazla 2 eş zamanlı istek.
    /// </summary>
    private async Task<string?> FetchDetailViaFlareSolverrAsync(
        string absoluteUrl,
        AkakceClientOptions options,
        CancellationToken cancellationToken)
    {
        await _flareSolverrDetailSem.WaitAsync(cancellationToken);
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(130) };

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                cmd = "request.get",
                url = absoluteUrl,
                maxTimeout = 90000,
                session = "akakce-detail"
            });

            _logger.LogDebug("Akakce FlareSolverr detail: {Url}", absoluteUrl);

            var resp = await http.PostAsync(
                options.FlareSolverrUrl!.TrimEnd('/') + "/v1",
                new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Akakce FlareSolverr detail HTTP {Status} → {Url}", (int)resp.StatusCode, absoluteUrl);
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = System.Text.Json.JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("solution", out var solution))
            {
                _logger.LogWarning("Akakce FlareSolverr detail: 'solution' yok → {Url}", absoluteUrl);
                return null;
            }

            var html = solution.TryGetProperty("response", out var r) ? r.GetString() : null;
            _logger.LogDebug("Akakce FlareSolverr detail: {HtmlLen} karakter → {Url}", html?.Length ?? 0, absoluteUrl);
            return html;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Akakce FlareSolverr detail hatası → {Url}", absoluteUrl);
            return null;
        }
        finally
        {
            _flareSolverrDetailSem.Release();
        }
    }

    private T? RunWithDriver<T>(
        AkakceClientOptions options,
        Func<IWebDriver, T?> work,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IWebDriver? driver = null;
        try
        {
            driver = CreateDriver(options);
            using var _ = cancellationToken.Register(() =>
            {
                try { driver?.Quit(); } catch (Exception ex) { _logger.LogTrace(ex, "Akakce Selenium cancel Quit"); }
            });

            return work(driver);
        }
        finally
        {
            try { driver?.Quit(); } catch (Exception ex) { _logger.LogDebug(ex, "Akakce Selenium Quit"); }
            driver?.Dispose();
        }
    }

    private IWebDriver CreateDriver(AkakceClientOptions options)
    {
        var chromeOptions = BuildChromeOptions(options);
        if (!string.IsNullOrWhiteSpace(options.SeleniumGridUrl))
        {
            var gridUri = new Uri(options.SeleniumGridUrl!.TrimEnd('/'));
            var commandTimeout = TimeSpan.FromMilliseconds(Math.Max(30_000, options.SeleniumCommandTimeoutMs));
            var driver = new RemoteWebDriver(gridUri, chromeOptions.ToCapabilities(), commandTimeout);
            ConfigureTimeouts(driver, options);
            return driver;
        }

        var service = ChromeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;
        var local = new ChromeDriver(service, chromeOptions, TimeSpan.FromMilliseconds(Math.Max(60_000, options.PageLoadTimeoutMs * 2)));
        ConfigureTimeouts(local, options);
        return local;
    }

    private static void ConfigureTimeouts(IWebDriver driver, AkakceClientOptions options)
    {
        var pageLoad = TimeSpan.FromMilliseconds(Math.Max(15_000, options.PageLoadTimeoutMs));
        driver.Manage().Timeouts().PageLoad = pageLoad;
        driver.Manage().Timeouts().AsynchronousJavaScript = pageLoad;
    }

    private static ChromeOptions BuildChromeOptions(AkakceClientOptions options)
    {
        var chromeOptions = new ChromeOptions
        {
            PageLoadStrategy = PageLoadStrategy.Eager,
        };

        if (options.SeleniumHeadless)
        {
            chromeOptions.AddArgument("--headless=new");
        }

        if (!string.IsNullOrWhiteSpace(options.SeleniumChromeBinaryPath))
        {
            chromeOptions.BinaryLocation = options.SeleniumChromeBinaryPath.Trim();
        }

        foreach (var arg in BuildChromeArgs(options))
        {
            chromeOptions.AddArgument(arg);
        }

        if (options.BlockImages)
        {
            chromeOptions.AddArgument("--blink-settings=imagesEnabled=false");
        }

        if (options.BlockFonts)
        {
            chromeOptions.AddArgument("--disable-remote-fonts");
        }

        chromeOptions.AddArgument($"--user-agent={options.UserAgent}");
        chromeOptions.AddArgument("--window-size=1920,1080");

        if (!string.IsNullOrWhiteSpace(options.ProxyUrl))
        {
            chromeOptions.AddArgument($"--proxy-server={options.ProxyUrl}");
        }

        var contentSettings = new Dictionary<string, object>();
        if (options.BlockImages)
        {
            contentSettings["images"] = 2;
        }

        if (options.BlockStyles)
        {
            contentSettings["stylesheets"] = 2;
        }

        if (contentSettings.Count > 0)
        {
            chromeOptions.AddUserProfilePreference("profile.managed_default_content_settings", contentSettings);
            chromeOptions.AddUserProfilePreference("profile.default_content_setting_values", contentSettings);
        }

        return chromeOptions;
    }

    private static string[] BuildChromeArgs(AkakceClientOptions options)
    {
        if (options.ChromiumExtraArgs is { Count: > 0 })
        {
            return options.ChromiumExtraArgs.ToArray();
        }

        return new[]
        {
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--disable-blink-features=AutomationControlled",
            "--disable-extensions",
            "--disable-background-networking",
            "--disable-default-apps",
            "--disable-component-update",
            "--disable-translate",
            "--disable-sync",
            "--metrics-recording-only",
            "--mute-audio",
            "--no-default-browser-check",
            "--no-first-run",
            "--password-store=basic",
        };
    }

    private void PrepareAndNavigate(IWebDriver driver, string absoluteUrl, AkakceClientOptions options)
    {
        if (Uri.TryCreate(options.BaseUrl.TrimEnd('/'), UriKind.Absolute, out var origin))
        {
            try
            {
                driver.Navigate().GoToUrl(origin.ToString());

                // FlareSolverr: Cloudflare clearance cookie'lerini enjekte et
                if (!string.IsNullOrWhiteSpace(options.FlareSolverrUrl))
                {
                    TryInjectFlareSolverrCookies(driver, options.FlareSolverrUrl, origin.ToString(), origin.Host);
                }

                TryAddCookies(driver, origin.Host, options);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Akakce Selenium origin preload skipped");
            }
        }

        try
        {
            driver.Navigate().GoToUrl(absoluteUrl);
        }
        catch (WebDriverException ex)
        {
            _logger.LogDebug(ex, "Akakce Selenium first navigation failed, retrying");
            Thread.Sleep(500);
            driver.Navigate().GoToUrl(absoluteUrl);
        }
    }

    /// <summary>
    /// FlareSolverr container API'sini çağırır; dönen Cloudflare clearance cookie'lerini
    /// mevcut Selenium oturumuna enjekte eder. 20 dakika boyunca cache'lenir (paralel session başına
    /// tek bir FlareSolverr Chrome örneği açılır). Hata durumunda scraping yine de devam eder.
    /// </summary>
    private void TryInjectFlareSolverrCookies(IWebDriver driver, string flareSolverrUrl, string targetUrl, string domain)
    {
        try
        {
            // Cache geçerliyse doğrudan inject et, FlareSolverr'a gitme.
            List<(string name, string value, string cookieDomain, string path)>? cookieList = null;

            _flareSolverrSem.Wait();
            try
            {
                if (_cachedCookieHeader != null && DateTime.UtcNow - _cookieCachedAt < _cookieTtl)
                {
                    _logger.LogDebug("Akakce FlareSolverr: cache'ten cookie kullanılıyor (age={Age:0}s)",
                        (DateTime.UtcNow - _cookieCachedAt).TotalSeconds);
                    InjectCookiesFromCachedHeader(driver, _cachedCookieHeader!, domain);
                    return;
                }

                // Cache yok veya süresi dolmuş — FlareSolverr'a tek istek at.
                using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(120) };

                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    cmd = "request.get",
                    url = targetUrl,
                    maxTimeout = 90000
                });
                var requestContent = new System.Net.Http.StringContent(
                    payload, System.Text.Encoding.UTF8, "application/json");

                _logger.LogInformation("Akakce FlareSolverr: {Url} için Cloudflare cookie alınıyor...", targetUrl);

                var response = httpClient
                    .PostAsync(flareSolverrUrl.TrimEnd('/') + "/v1", requestContent)
                    .GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Akakce FlareSolverr: HTTP {Status} — cookie alınamadı, standart scraping devam ediyor",
                        (int)response.StatusCode);
                    return;
                }

                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = System.Text.Json.JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("solution", out var solution)
                    || !solution.TryGetProperty("cookies", out var cookiesEl))
                {
                    _logger.LogWarning("Akakce FlareSolverr: yanıtta cookie alanı yok");
                    return;
                }

                cookieList = new List<(string, string, string, string)>();
                foreach (var c in cookiesEl.EnumerateArray())
                {
                    var name  = c.GetProperty("name").GetString() ?? "";
                    var value = c.GetProperty("value").GetString() ?? "";
                    var cd    = c.TryGetProperty("domain", out var d) ? (d.GetString() ?? domain) : domain;
                    var path  = c.TryGetProperty("path", out var p) ? (p.GetString() ?? "/") : "/";
                    if (!string.IsNullOrEmpty(name))
                        cookieList.Add((name, value, cd, path));
                }

                // Cache güncelle
                _cachedCookieHeader = string.Join(";", cookieList.Select(c => $"{c.name}={c.value}"));
                _cookieCachedAt = DateTime.UtcNow;
                _logger.LogInformation("Akakce FlareSolverr: {Count} cookie alındı ve cache'lendi ✓", cookieList.Count);
            }
            finally
            {
                _flareSolverrSem.Release();
            }

            if (cookieList != null)
            {
                var injected = 0;
                foreach (var (name, value, cd, path) in cookieList)
                {
                    try
                    {
                        driver.Manage().Cookies.AddCookie(new Cookie(name, value, cd, path, null));
                        injected++;
                    }
                    catch { /* geçersiz cookie */ }
                }
                _logger.LogInformation("Akakce FlareSolverr: {Count} cookie Selenium'a enjekte edildi ✓", injected);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Akakce FlareSolverr: cookie alma hatası — standart scraping devam ediyor");
        }
    }

    private static void InjectCookiesFromCachedHeader(IWebDriver driver, string cookieHeader, string domain)
    {
        foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var name  = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            try { driver.Manage().Cookies.AddCookie(new Cookie(name, value, domain, "/", null)); }
            catch { /* ignore */ }
        }
    }

    private void TryAddCookies(IWebDriver driver, string host, AkakceClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Cookie))
        {
            return;
        }

        var cookieDomain = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host : "." + host;
        foreach (var part in options.Cookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            try
            {
                driver.Manage().Cookies.AddCookie(new Cookie(name, value, cookieDomain, "/", null));
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Akakce Selenium cookie add failed {Name}", name);
            }
        }
    }

    private void DismissCookieBanner(IWebDriver driver)
    {
        try
        {
            var buttons = driver.FindElements(By.XPath("//button[contains(translate(normalize-space(.),'KABULTAMAM','kabultamam'),'kabul') or normalize-space(.)='Tamam']"));
            foreach (var b in buttons)
            {
                try
                {
                    if (!b.Displayed)
                    {
                        continue;
                    }

                    b.Click();
                    Thread.Sleep(120);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Akakce cookie banner click failed");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Akakce cookie banner scan failed");
        }
    }

    private void ScrollPage(IWebDriver driver, AkakceClientOptions options, CancellationToken cancellationToken)
    {
        if (driver is not IJavaScriptExecutor js)
        {
            return;
        }

        var passes = Math.Clamp(options.ScrollPasses, 1, 20);
        var pause = Math.Clamp(options.ScrollPauseMs, 80, 4000);
        for (var i = 0; i < passes; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
            Thread.Sleep(pause);
            js.ExecuteScript("window.scrollBy(0, -120);");
            Thread.Sleep(80);
        }

        js.ExecuteScript("window.scrollTo(0, 0);");
        Thread.Sleep(120);
    }

    private static void WaitForCss(
        IWebDriver driver,
        string cssSelector,
        int timeoutMs,
        CancellationToken cancellationToken,
        bool throwOnTimeout)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(2000, timeoutMs));
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                driver.FindElement(By.CssSelector(cssSelector));
                return;
            }
            catch (NoSuchElementException)
            {
            }
            catch (StaleElementReferenceException)
            {
            }

            Thread.Sleep(150);
        }

        if (throwOnTimeout)
        {
            throw new WebDriverException($"Element not found within {timeoutMs} ms: {cssSelector}");
        }
    }
}
