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

    public AkakceSeleniumPageFetcher(ILogger<AkakceSeleniumPageFetcher> logger)
    {
        _logger = logger;
    }

    public Task<string?> TryGetListingHtmlAsync(
        string absoluteUrl,
        AkakceClientOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
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

    public Task<string?> TryGetProductDetailHtmlAsync(
        string absoluteProductUrl,
        AkakceClientOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
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
