using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Selenium WebDriver (Chrome) ile Cimri sayfaları yükler. İndirimli ürünler listesi ve ürün detay
/// sayfasındaki dinamik içerik için "Daha Fazla Ürün Göster" ve "+9 Fiyat Teklifini Gör" butonlarına basar.
/// </summary>
public class CimriSeleniumPageFetcher : ITransientDependency
{
    private readonly ILogger<CimriSeleniumPageFetcher> _logger;

    public CimriSeleniumPageFetcher(ILogger<CimriSeleniumPageFetcher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// İndirimli ürünler listesini açar ve verilen sayı kadar "Daha Fazla Ürün Göster" tıklar; sonuçtaki HTML'i döndürür.
    /// </summary>
    public Task<string?> TryGetListingHtmlAsync(
        string absoluteUrl,
        int maxLoadMoreClicks,
        CimriClientOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => RunWithDriver(
                options,
                driver =>
                {
                    PrepareAndNavigate(driver, absoluteUrl, options);
                    WaitForCss(driver, "#productListContainer", options.DetailWaitTimeoutMs, cancellationToken, throwOnTimeout: false);
                    DismissCookieBanner(driver);
                    ScrollPage(driver, options, cancellationToken);

                    var clicks = Math.Max(0, maxLoadMoreClicks);
                    for (var i = 0; i < clicks; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!ClickLoadMoreButton(driver))
                        {
                            _logger.LogInformation(
                                "Cimri Selenium: 'Daha Fazla Ürün Göster' butonu artık yok, sayfalama tamamlandı (turn={Turn})",
                                i);
                            break;
                        }

                        Thread.Sleep(Math.Max(300, options.LoadMoreClickPauseMs));
                        ScrollPage(driver, options, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return driver.PageSource;
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Ürün detay sayfasını açar; gerekirse "+9 Fiyat Teklifini Gör" butonuna ardı ardına tıklayarak tüm teklifleri DOM'a serer.
    /// </summary>
    public Task<string?> TryGetProductDetailHtmlAsync(
        string absoluteProductUrl,
        bool expandAllOffers,
        CimriClientOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => RunWithDriver(
                options,
                driver =>
                {
                    PrepareAndNavigate(driver, absoluteProductUrl, options);
                    WaitForCss(driver, "section[id=fiyatlar], .kUEZW", options.DetailWaitTimeoutMs, cancellationToken, throwOnTimeout: false);
                    DismissCookieBanner(driver);
                    ScrollPage(driver, options, cancellationToken);

                    if (expandAllOffers)
                    {
                        ExpandAllOfferGroups(driver, options, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return driver.PageSource;
                },
                cancellationToken),
            cancellationToken);
    }

    private bool ClickLoadMoreButton(IWebDriver driver)
    {
        IWebElement? button = null;
        try
        {
            button = driver
                .FindElements(By.CssSelector("a[title^='İndirimli'][href*='page=']"))
                .FirstOrDefault(b => (b.Text ?? "").Contains("Daha Fazla", StringComparison.OrdinalIgnoreCase));

            if (button == null)
            {
                button = driver
                    .FindElements(By.TagName("a"))
                    .FirstOrDefault(a => (a.Text ?? "").Contains("Daha Fazla Ürün Göster", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri Selenium: Daha Fazla butonu aranırken hata");
        }

        if (button == null || !button.Displayed)
        {
            return false;
        }

        try
        {
            ScrollIntoView(driver, button);
            Thread.Sleep(120);
            button.Click();
            return true;
        }
        catch (ElementClickInterceptedException)
        {
            try
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", button);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cimri Selenium: JS fallback click başarısız");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cimri Selenium: Daha Fazla click başarısız");
            return false;
        }
    }

    private void ExpandAllOfferGroups(IWebDriver driver, CimriClientOptions options, CancellationToken cancellationToken)
    {
        const int maxClicks = 30;
        for (var i = 0; i < maxClicks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IWebElement? expandButton = null;
            try
            {
                expandButton = driver
                    .FindElements(By.CssSelector("section#fiyatlar div.pxJQ_, section#fiyatlar [class*='pxJQ']"))
                    .FirstOrDefault(IsVisibleExpandButton);

                if (expandButton == null)
                {
                    expandButton = driver
                        .FindElements(By.XPath("//div[contains(text(),'Fiyat Teklifini Gör') or .//*[contains(text(),'Fiyat Teklifini Gör')]]"))
                        .FirstOrDefault(IsVisibleExpandButton);
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Cimri Selenium: '+X Fiyat Teklifini Gör' aranırken hata");
            }

            if (expandButton == null)
            {
                _logger.LogDebug("Cimri Selenium: tüm teklifler genişletildi (tur={Turn})", i);
                return;
            }

            try
            {
                ScrollIntoView(driver, expandButton);
                Thread.Sleep(120);
                expandButton.Click();
            }
            catch (ElementClickInterceptedException)
            {
                try
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", expandButton);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Cimri Selenium: '+X Fiyat Teklifini Gör' JS click başarısız");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cimri Selenium: '+X Fiyat Teklifini Gör' click başarısız");
                return;
            }

            Thread.Sleep(Math.Max(300, options.ExpandOffersClickPauseMs));
        }
    }

    private static bool IsVisibleExpandButton(IWebElement element)
    {
        try
        {
            if (!element.Displayed)
            {
                return false;
            }

            var text = element.Text ?? string.Empty;
            return text.Contains("Fiyat Teklifini Gör", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ScrollIntoView(IWebDriver driver, IWebElement element)
    {
        if (driver is IJavaScriptExecutor js)
        {
            js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", element);
        }
    }

    private void DismissCookieBanner(IWebDriver driver)
    {
        try
        {
            var buttons = driver.FindElements(By.CssSelector("button"))
                .Where(b =>
                {
                    try
                    {
                        var t = (b.Text ?? string.Empty).Trim();
                        return b.Displayed && (
                            t.Equals("Tamam", StringComparison.OrdinalIgnoreCase)
                            || t.Equals("Kabul Et", StringComparison.OrdinalIgnoreCase)
                            || t.Contains("Kabul", StringComparison.OrdinalIgnoreCase));
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();

            foreach (var b in buttons)
            {
                try
                {
                    b.Click();
                    Thread.Sleep(120);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Cimri Selenium: cookie banner click başarısız");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri Selenium: cookie banner taraması");
        }
    }

    private void ScrollPage(IWebDriver driver, CimriClientOptions options, CancellationToken cancellationToken)
    {
        if (driver is not IJavaScriptExecutor js)
        {
            return;
        }

        var passes = Math.Clamp(options.ScrollPasses, 1, 30);
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

    private T? RunWithDriver<T>(
        CimriClientOptions options,
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
                try
                {
                    driver?.Quit();
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Cimri Selenium: iptalde Quit");
                }
            });

            return work(driver);
        }
        finally
        {
            try
            {
                driver?.Quit();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cimri Selenium: Quit");
            }

            driver?.Dispose();
        }
    }

    private ChromeDriver CreateDriver(CimriClientOptions options)
    {
        var chromeOptions = new ChromeOptions();

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

        chromeOptions.AddArgument($"--user-agent={options.UserAgent}");
        chromeOptions.AddArgument("--window-size=1920,1080");

        var service = ChromeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;

        var launchTimeoutMs = Math.Max(60_000, options.PageLoadTimeoutMs * 2);
        var driver = new ChromeDriver(service, chromeOptions, TimeSpan.FromMilliseconds(launchTimeoutMs));

        var pageLoad = TimeSpan.FromMilliseconds(Math.Max(15_000, options.PageLoadTimeoutMs));
        driver.Manage().Timeouts().PageLoad = pageLoad;
        driver.Manage().Timeouts().AsynchronousJavaScript = pageLoad;

        return driver;
    }

    private static string[] BuildChromeArgs(CimriClientOptions options)
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
        };
    }

    private void PrepareAndNavigate(IWebDriver driver, string absoluteUrl, CimriClientOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl.TrimEnd('/'), UriKind.Absolute, out var origin))
        {
            driver.Navigate().GoToUrl(absoluteUrl);
            return;
        }

        try
        {
            driver.Navigate().GoToUrl(origin.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri Selenium: anasayfa ön yükleme atlandı");
        }

        TryAddCookies(driver, origin.Host, options);

        try
        {
            driver.Navigate().GoToUrl(absoluteUrl);
        }
        catch (WebDriverException ex)
        {
            _logger.LogDebug(ex, "Cimri Selenium: ilk yükleme hatası, ikinci deneme");
            Thread.Sleep(300);
            driver.Navigate().GoToUrl(absoluteUrl);
        }
    }

    private void TryAddCookies(IWebDriver driver, string host, CimriClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Cookie))
        {
            return;
        }

        var cookieDomain = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host
            : "." + host;

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
                _logger.LogTrace(ex, "Cimri Selenium: cookie eklenemedi {Name}", name);
            }
        }
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
