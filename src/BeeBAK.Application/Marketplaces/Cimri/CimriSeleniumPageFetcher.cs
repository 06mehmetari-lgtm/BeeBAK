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

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Selenium WebDriver (Chrome) ile Cimri sayfaları yükler. İndirimli ürünler listesi ve ürün detay
/// sayfasındaki dinamik içerik için "Daha Fazla Ürün Göster" ve "+9 Fiyat Teklifini Gör" butonlarına basar.
/// </summary>
public class CimriSeleniumPageFetcher : ITransientDependency
{
    private readonly ILogger<CimriSeleniumPageFetcher> _logger;
    private readonly ICimriOfferUrlCache? _offerUrlCache;

    public CimriSeleniumPageFetcher(
        ILogger<CimriSeleniumPageFetcher> logger,
        ICimriOfferUrlCache? offerUrlCache = null)
    {
        _logger = logger;
        _offerUrlCache = offerUrlCache;
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
        return TryGetProductDetailAsync(absoluteProductUrl, expandAllOffers, options, cancellationToken)
            .ContinueWith(t => t.Result?.Html, cancellationToken);
    }

    /// <summary>
    /// Ürün detay sayfasını açar ve hem rendered HTML'i hem de teklif kartlarındaki tıklama akışından
    /// JS shim'iyle yakalanmış mağaza redirect URL'lerini (sıraya göre) döndürür.
    /// </summary>
    public Task<CimriProductDetailFetchResult?> TryGetProductDetailAsync(
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
                    var totalSw = System.Diagnostics.Stopwatch.StartNew();

                    var navSw = System.Diagnostics.Stopwatch.StartNew();
                    PrepareAndNavigate(driver, absoluteProductUrl, options);
                    WaitForCss(driver, "section[id=fiyatlar], .kUEZW", options.DetailWaitTimeoutMs, cancellationToken, throwOnTimeout: false);
                    navSw.Stop();

                    var bannerSw = System.Diagnostics.Stopwatch.StartNew();
                    DismissCookieBanner(driver);
                    bannerSw.Stop();

                    var scrollSw = System.Diagnostics.Stopwatch.StartNew();
                    ScrollPage(driver, options, cancellationToken);
                    scrollSw.Stop();

                    var expandSw = System.Diagnostics.Stopwatch.StartNew();
                    if (expandAllOffers)
                    {
                        ExpandAllOfferGroups(driver, options, cancellationToken);
                    }
                    expandSw.Stop();

                    cancellationToken.ThrowIfCancellationRequested();

                    var captureSw = System.Diagnostics.Stopwatch.StartNew();
                    var capturedOfferUrls = TryCaptureOfferRedirectUrls(driver);
                    captureSw.Stop();

                    var html = driver.PageSource;

                    _logger.LogInformation(
                        "Cimri PDP timing: nav={NavMs}ms banner={BannerMs}ms scroll={ScrollMs}ms expand={ExpandMs}ms capture={CaptureMs}ms total-pre-resolve={TotalMs}ms",
                        navSw.ElapsedMilliseconds, bannerSw.ElapsedMilliseconds, scrollSw.ElapsedMilliseconds,
                        expandSw.ElapsedMilliseconds, captureSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds);


                    // PDP yüklü iken aynı Selenium oturumunda /offer/{id} redirect'lerini yeni tab'da
                    // takip ederek asıl mağaza URL'lerini hasat et. Cimri tarayıcıdan gelen redirect'i
                    // (HEAD/GET'in aksine) 30x ile tamamlıyor — bu yüzden tek güvenilir yol burası.
                    var resolvedMerchantUrls = options.ResolveOfferUrlsViaSelenium
                        ? ResolveOfferUrlsViaBrowser(driver, capturedOfferUrls, options, cancellationToken)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    return new CimriProductDetailFetchResult
                    {
                        Html = html,
                        CapturedOfferUrls = capturedOfferUrls,
                        ResolvedMerchantUrls = resolvedMerchantUrls,
                    };
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Teklif kartlarındaki ".nzsL3" tıklama butonlarına basıp window.open / window.location çağrılarından
    /// mağaza yönlendirme URL'lerini hasat eder. Yeni sekme açılmaz; orijinal navigasyon iptal edilir.
    /// </summary>
    private List<string> TryCaptureOfferRedirectUrls(IWebDriver driver)
    {
        var urls = new List<string>();
        if (driver is not IJavaScriptExecutor js)
        {
            return urls;
        }

        const string script = @"
            const captured = [];
            const origOpen = window.open;
            const origAssign = window.location.assign && window.location.assign.bind(window.location);
            const origReplace = window.location.replace && window.location.replace.bind(window.location);
            try {
                window.open = function(u){ if (u) captured.push(String(u)); return null; };
                try { window.location.assign = function(u){ if (u) captured.push(String(u)); }; } catch (e) {}
                try { window.location.replace = function(u){ if (u) captured.push(String(u)); }; } catch (e) {}

                const blockNav = (e) => { e.preventDefault(); e.stopImmediatePropagation(); };
                window.addEventListener('beforeunload', blockNav, true);

                const hijackAnchorClick = (a, list) => {
                    const href = a.getAttribute && a.getAttribute('href');
                    if (href) list.push(href);
                };
                document.addEventListener('click', function(ev){
                    const a = ev.target && ev.target.closest && ev.target.closest('a[href]');
                    if (a) {
                        ev.preventDefault();
                        ev.stopImmediatePropagation();
                        const href = a.getAttribute('href');
                        if (href) captured.push(href);
                    }
                }, true);

                const cards = document.querySelectorAll('section#fiyatlar div.o1fRW, [data-offer]');
                cards.forEach((card, idx) => {
                    let target =
                        card.querySelector('button.nzsL3') ||
                        card.querySelector('button[aria-label=""teklif kart\u0131 linki""]') ||
                        card.querySelector('a[href]') ||
                        card.querySelector('button');
                    if (!target) { captured.push(''); return; }
                    const before = captured.length;
                    try { target.click(); } catch (e) {}
                    if (captured.length === before) {
                        // tıklama sonrası bir şey yakalayamadıysak, kart üstündeki tıklama izleme
                        // anchor varsa onu hijack et; yoksa boş bırak.
                        const a = card.querySelector('a[href]');
                        if (a) hijackAnchorClick(a, captured);
                        else captured.push('');
                    } else {
                        // birden fazla URL yakalandıysa son yakalananı tek kart için tut.
                        const last = captured[captured.length - 1];
                        captured.length = before;
                        captured.push(last);
                    }
                });
            } finally {
                window.open = origOpen;
                if (origAssign) { try { window.location.assign = origAssign; } catch (e) {} }
                if (origReplace) { try { window.location.replace = origReplace; } catch (e) {} }
            }
            return captured;
        ";

        try
        {
            var result = js.ExecuteScript(script);
            if (result is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    urls.Add(item?.ToString() ?? string.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cimri Selenium: teklif redirect URL hasadı başarısız");
        }

        return urls;
    }

    /// <summary>
    /// Cimri'nin <c>https://www.cimri.com/offer/{id}?...</c> redirect URL'lerini, PDP'nin yüklü olduğu
    /// aynı driver oturumunda <b>tüm tab'ları paralel açıp</b> her birinin redirect chain'inin host
    /// cimri.com dışına çıkmasını polling ile takip ederek çözer. Sıralı versiyona göre 8-12x hızlanma.
    /// HttpClient bu URL'lere 403 yiyor; tarayıcı oturumunda redirect chain sorunsuz tamamlanıyor.
    /// </summary>
    private Dictionary<string, string> ResolveOfferUrlsViaBrowser(
        IWebDriver driver,
        IReadOnlyList<string> capturedOfferUrls,
        CimriClientOptions options,
        CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (capturedOfferUrls == null || capturedOfferUrls.Count == 0)
        {
            return resolved;
        }

        if (driver is not IJavaScriptExecutor js)
        {
            return resolved;
        }

        string mainHandle;
        try
        {
            mainHandle = driver.CurrentWindowHandle;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri Selenium: ana tab handle alınamadı, offer resolve atlandı");
            return resolved;
        }

        var maxToResolve = options.MaxOfferUrlsToResolveViaSelenium > 0
            ? options.MaxOfferUrlsToResolveViaSelenium
            : int.MaxValue;

        // 1) Cap + dedup + yerinde kullan-ve-atla.
        var pending = new List<string>(Math.Min(capturedOfferUrls.Count, 32));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in capturedOfferUrls)
        {
            if (pending.Count >= maxToResolve)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(raw) || !seen.Add(raw))
            {
                continue;
            }

            // Doğrudan mağaza URL'si yakalandıysa (cimri.com /offer/ değil) ek tab açmaya gerek yok.
            if (!CimriCrawlHost.IsCimriOfferRedirectUrl(raw, options))
            {
                resolved[raw] = raw;
                continue;
            }

            pending.Add(raw);
        }

        if (pending.Count == 0)
        {
            return resolved;
        }

        // 1.b) Redis cache'te zaten çözülmüş olanları doğrudan al; kalanları Selenium ile çözeceğiz.
        if (_offerUrlCache != null)
        {
            var stillPending = new List<string>(pending.Count);
            foreach (var url in pending)
            {
                string? cached = null;
                try
                {
                    cached = _offerUrlCache.TryGetAsync(url, cancellationToken).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Cimri Selenium: offer-url cache lookup hatası ({Url})", url);
                }

                if (!string.IsNullOrWhiteSpace(cached) && !CimriCrawlHost.IsCimriOfferRedirectUrl(cached!, options))
                {
                    resolved[url] = cached!;
                }
                else
                {
                    stillPending.Add(url);
                }
            }

            if (stillPending.Count != pending.Count)
            {
                _logger.LogDebug(
                    "Cimri Selenium: offer-url cache'ten {Hit}/{Total} hit; {Miss} URL Selenium ile çözülecek",
                    pending.Count - stillPending.Count,
                    pending.Count,
                    stillPending.Count);
            }

            pending = stillPending;
        }

        if (pending.Count == 0)
        {
            return resolved;
        }

        // 2) Tüm tab'ları ardarda hızlıca aç ve handle'ları topla.
        //    window.open arasına kısa bir delay koyarak Chrome'un yeni handle'ı raporlamasına vakit ver.
        var openWaitFor = new HashSet<string>(driver.WindowHandles, StringComparer.Ordinal);
        var pendingTabs = new List<PendingOfferTab>(pending.Count);

        // ChromeDriver'da SwitchTo().Window(handle) target tab'ın page-load tamamlanmasını bekler.
        // Mağaza sayfaları (Amazon/HepsiBurada/MediaMarkt vb.) yavaş yüklendiği için bu davranış polling
        // turunu tek tab başına 4-5 sn'ye uzatıyor. PageLoadTimeout'u geçici olarak kısaltarak SwitchTo
        // bu süreden sonra TimeoutException ile hızlı dönecek; biz redirect'in YAPILMIŞ olmasıyla
        // ilgileniyoruz, sayfanın tamamen yüklenmesiyle değil.
        TimeSpan? originalPageLoadTimeout = null;
        try
        {
            originalPageLoadTimeout = driver.Manage().Timeouts().PageLoad;
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(2);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri Selenium: PageLoad timeout kısaltılamadı");
        }

        try
        {
            foreach (var url in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    js.ExecuteScript("window.open(arguments[0], '_blank', 'noopener=no,noreferrer=no');", url);
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Cimri Selenium: paralel window.open başarısız ({Url})", url);
                    continue;
                }

                var handle = WaitForNewWindowHandle(driver, openWaitFor, TimeSpan.FromMilliseconds(600));
                if (handle == null)
                {
                    _logger.LogTrace("Cimri Selenium: yeni tab handle alınamadı ({Url})", url);
                    continue;
                }

                openWaitFor.Add(handle);
                pendingTabs.Add(new PendingOfferTab(url, handle));
            }

            if (pendingTabs.Count == 0)
            {
                return resolved;
            }

            // 3) Tüm tab'ları aynı deadline ile paralel polla.
            //    PageLoadTimeout=2sn ayarladığımız için SwitchTo() bu süreden sonra hızlı dönüyor.
            //    Her tab için sonra JS ile window.stop() çağırıp URL'i oku.
            var deadlineMs = Math.Max(2000, options.OfferResolveTimeoutMs);
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(deadlineMs);
            var pollInterval = TimeSpan.FromMilliseconds(150);
            var iterations = 0;

            _logger.LogInformation(
                "Cimri offer-resolve: polling başlıyor (tabs={Tabs}, deadlineMs={DeadlineMs})",
                pendingTabs.Count, deadlineMs);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                iterations++;

                var allDone = true;
                foreach (var tab in pendingTabs)
                {
                    if (tab.Resolved)
                    {
                        continue;
                    }

                    try
                    {
                        driver.SwitchTo().Window(tab.Handle);
                    }
                    catch (NoSuchWindowException)
                    {
                        tab.Resolved = true;
                        continue;
                    }
                    catch (WebDriverTimeoutException)
                    {
                        // SwitchTo timeout: sayfa hâlâ yükleniyor; URL okumayı yine de deneriz.
                    }
                    catch
                    {
                        allDone = false;
                        continue;
                    }

                    // window.stop() ile yarım kalan resource'ları öldür → bir sonraki SwitchTo hızlı.
                    try { js.ExecuteScript("try { window.stop(); } catch (e) {}"); } catch { }

                    string current;
                    try
                    {
                        var raw = js.ExecuteScript("return document.location && document.location.href;") as string;
                        current = raw ?? string.Empty;
                    }
                    catch (WebDriverException)
                    {
                        allDone = false;
                        continue;
                    }

                    if (string.IsNullOrEmpty(current)
                        || string.Equals(current, "about:blank", StringComparison.OrdinalIgnoreCase)
                        || CimriCrawlHost.IsCimriOfferRedirectUrl(current, options))
                    {
                        allDone = false;
                        continue;
                    }

                    tab.FinalUrl = current;
                    tab.Resolved = true;
                }

                if (allDone)
                {
                    break;
                }

                Thread.Sleep(pollInterval);
            }

            _logger.LogInformation(
                "Cimri offer-resolve: polling bitti (iterasyon={Iter}, resolved={Resolved}/{Total})",
                iterations, pendingTabs.Count(t => t.Resolved && !string.IsNullOrEmpty(t.FinalUrl)), pendingTabs.Count);

            // 4) Resolve edilemeyenler için son bir okuma denemesi (timeout doldu ama URL kullanılabilir olabilir).
            foreach (var tab in pendingTabs)
            {
                if (tab.Resolved && !string.IsNullOrEmpty(tab.FinalUrl))
                {
                    continue;
                }

                try
                {
                    driver.SwitchTo().Window(tab.Handle);
                    var lastSeen = js.ExecuteScript("return document.location && document.location.href;") as string;
                    if (!string.IsNullOrEmpty(lastSeen) && !CimriCrawlHost.IsCimriOfferRedirectUrl(lastSeen!, options))
                    {
                        tab.FinalUrl = lastSeen;
                        tab.Resolved = true;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // 5) Resolve sonuçlarını birleştir + cache'e yaz.
            foreach (var tab in pendingTabs)
            {
                if (string.IsNullOrEmpty(tab.FinalUrl) || CimriCrawlHost.IsCimriOfferRedirectUrl(tab.FinalUrl, options))
                {
                    continue;
                }

                resolved[tab.OriginalUrl] = tab.FinalUrl!;

                if (_offerUrlCache != null)
                {
                    try
                    {
                        _offerUrlCache.SetAsync(tab.OriginalUrl, tab.FinalUrl!, cancellationToken)
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Cimri Selenium: offer-url cache yazma ({Url})", tab.OriginalUrl);
                    }
                }
            }
        }
        finally
        {
            // 6) Tüm açılmış offer tab'larını kapat ve ana tab'a geri dön.
            CloseAllExtraTabs(driver, mainHandle);

            // PageLoadTimeout'u eski haline döndür (PDP'nin ilerideki navigation komutlarına etki etmesin).
            if (originalPageLoadTimeout.HasValue)
            {
                try
                {
                    driver.Manage().Timeouts().PageLoad = originalPageLoadTimeout.Value;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Cimri Selenium: PageLoad timeout geri yüklenirken hata");
                }
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Cimri offer-resolve: {Resolved} mağaza URL'si {ElapsedMs} ms'de çözüldü (pending={Pending}, captured={Captured})",
            resolved.Count, sw.ElapsedMilliseconds, pending.Count, capturedOfferUrls.Count);

        return resolved;
    }

    private sealed class PendingOfferTab
    {
        public PendingOfferTab(string originalUrl, string handle)
        {
            OriginalUrl = originalUrl;
            Handle = handle;
        }

        public string OriginalUrl { get; }
        public string Handle { get; }
        public string? FinalUrl { get; set; }
        public bool Resolved { get; set; }
    }

    private static string? WaitForNewWindowHandle(
        IWebDriver driver,
        HashSet<string> beforeHandles,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                foreach (var h in driver.WindowHandles)
                {
                    if (!beforeHandles.Contains(h))
                    {
                        return h;
                    }
                }
            }
            catch
            {
                // race ile yarış olabilir; tekrar dene
            }

            Thread.Sleep(40);
        }

        return null;
    }

    private void CloseAllExtraTabs(IWebDriver driver, string mainHandle)
    {
        try
        {
            var handles = driver.WindowHandles;
            foreach (var h in handles)
            {
                if (h == mainHandle)
                {
                    continue;
                }

                try
                {
                    driver.SwitchTo().Window(h);
                    driver.Close();
                }
                catch
                {
                    // tab zaten kapanmış olabilir
                }
            }

            driver.SwitchTo().Window(mainHandle);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri Selenium: ana tab'a geri dönüş");
        }
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
        // Cimri PDP'lerde tipik olarak 1 (nadiren 2) "+9 Fiyat Teklifini Gör" butonu vardır.
        // Eski max=30 değeri, görünür kalan ama disabled bir buton senaryosunda 30 sn'lik kayba yol açıyordu.
        const int maxClicks = 3;
        var pause = Math.Max(120, options.ExpandOffersClickPauseMs);
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

            Thread.Sleep(pause);
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
            // Önceden tüm <button> elementleri topluyorduk; her PDP'de 30+ buton geliyor ve filtreleme
            // pahalıydı. XPath ile sadece metin eşleşmesini DOM tarafında yaptırarak 200-400 ms kazanç.
            var buttons = driver.FindElements(
                By.XPath(
                    "//button[contains(translate(normalize-space(.),'KABUL','kabul'),'kabul')"
                    + " or normalize-space(.)='Tamam']"));

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

    private IWebDriver CreateDriver(CimriClientOptions options)
    {
        var chromeOptions = BuildChromeOptions(options);

        if (!string.IsNullOrWhiteSpace(options.SeleniumGridUrl))
        {
            return CreateRemoteDriver(chromeOptions, options);
        }

        return CreateLocalChromeDriver(chromeOptions, options);
    }

    private static ChromeOptions BuildChromeOptions(CimriClientOptions options)
    {
        var chromeOptions = new ChromeOptions
        {
            // Eager: DOMContentLoaded'da page-load tamamlanmış sayılır; lazy script/image bekleme.
            // ~2-4 sn kazanç; WaitForCss zaten gerçek elementi bekliyor, regresyon riski düşük.
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
            // Content-settings'e ek belt-and-suspenders: Blink'ten direkt image yüklemeyi kapatır.
            chromeOptions.AddArgument("--blink-settings=imagesEnabled=false");
        }

        chromeOptions.AddArgument($"--user-agent={options.UserAgent}");
        chromeOptions.AddArgument("--window-size=1920,1080");

        ApplyContentBlockingPrefs(chromeOptions, options);

        return chromeOptions;
    }

    private static void ApplyContentBlockingPrefs(ChromeOptions chromeOptions, CimriClientOptions options)
    {
        // chrome://settings/content content_settings
        var contentSettings = new Dictionary<string, object>();
        if (options.BlockImages)
        {
            contentSettings["images"] = 2;
        }

        if (options.BlockStyles)
        {
            contentSettings["stylesheets"] = 2;
        }

        if (options.BlockFonts)
        {
            // Fontları doğrudan kapatan resmi flag yok; ancak performans için
            // disable-remote-fonts ve permissions üzerinden geçilebiliyor.
            chromeOptions.AddArgument("--disable-remote-fonts");
        }

        if (contentSettings.Count > 0)
        {
            var prefs = new Dictionary<string, object>
            {
                ["profile.managed_default_content_settings"] = contentSettings,
                ["profile.default_content_setting_values"] = contentSettings,
            };

            foreach (var kv in prefs)
            {
                chromeOptions.AddUserProfilePreference(kv.Key, kv.Value);
            }
        }
    }

    private IWebDriver CreateRemoteDriver(ChromeOptions chromeOptions, CimriClientOptions options)
    {
        var gridUri = new Uri(options.SeleniumGridUrl!.TrimEnd('/'));
        var commandTimeout = TimeSpan.FromMilliseconds(Math.Max(30_000, options.SeleniumCommandTimeoutMs));

        var driver = new RemoteWebDriver(gridUri, chromeOptions.ToCapabilities(), commandTimeout);

        var pageLoad = TimeSpan.FromMilliseconds(Math.Max(15_000, options.PageLoadTimeoutMs));
        driver.Manage().Timeouts().PageLoad = pageLoad;
        driver.Manage().Timeouts().AsynchronousJavaScript = pageLoad;

        return driver;
    }

    private IWebDriver CreateLocalChromeDriver(ChromeOptions chromeOptions, CimriClientOptions options)
    {
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
