using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri ürün detay sayfasını Selenium ile çekip <see cref="CimriProductDetailHtmlParser"/> ile parse eden ince katman.
/// Ayrıca URL geçerliliğini doğrular.
/// </summary>
public class CimriProductDetailScraper : ITransientDependency
{
    private readonly CimriSeleniumPageFetcher _seleniumPageFetcher;
    private readonly ICimriOfferUrlResolver _offerUrlResolver;
    private readonly IClock _clock;
    private readonly ILogger<CimriProductDetailScraper> _logger;

    public CimriProductDetailScraper(
        CimriSeleniumPageFetcher seleniumPageFetcher,
        ICimriOfferUrlResolver offerUrlResolver,
        IClock clock,
        ILogger<CimriProductDetailScraper> logger)
    {
        _seleniumPageFetcher = seleniumPageFetcher;
        _offerUrlResolver = offerUrlResolver;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CimriProductDetailExtract?> FetchAsync(
        string productUrl,
        bool expandAllOffers,
        CimriClientOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateProductUrl(productUrl, options);

        var fetchResult = await _seleniumPageFetcher.TryGetProductDetailAsync(
            productUrl,
            expandAllOffers,
            options,
            cancellationToken);

        if (fetchResult == null || string.IsNullOrWhiteSpace(fetchResult.Html))
        {
            _logger.LogWarning("Cimri PDP boş HTML döndü: {ProductUrl}", productUrl);
            return null;
        }

        var extract = CimriProductDetailHtmlParser.TryParse(fetchResult.Html, productUrl, _clock.Now);
        if (extract == null)
        {
            return null;
        }

        await PopulateMerchantUrlsAsync(extract, fetchResult, cancellationToken);
        return extract;
    }

    private async Task PopulateMerchantUrlsAsync(
        CimriProductDetailExtract extract,
        CimriProductDetailFetchResult fetchResult,
        CancellationToken cancellationToken)
    {
        var capturedOfferUrls = fetchResult.CapturedOfferUrls;
        var resolvedMap = fetchResult.ResolvedMerchantUrls
                          ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < extract.Offers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var offer = extract.Offers[i];
            var captured = i < capturedOfferUrls.Count ? capturedOfferUrls[i] : null;

            // Karttan zaten bir href yakaladıysak (eski parser yolu) onu da değerlendir.
            var seedUrl = !string.IsNullOrWhiteSpace(captured) ? captured : offer.OfferUrl;
            if (string.IsNullOrWhiteSpace(seedUrl))
            {
                continue;
            }

            offer.OfferUrl ??= seedUrl;

            // 1) Aynı Selenium oturumunda yeni tab ile çözülmüş gerçek mağaza URL'si varsa onu kullan.
            //    Bu en güvenilir yol: Cimri tarayıcı session'ında redirect chain'i doğru tamamlıyor.
            if (resolvedMap.TryGetValue(seedUrl, out var browserResolved)
                && !string.IsNullOrWhiteSpace(browserResolved)
                && !IsCimriOfferRedirect(browserResolved))
            {
                offer.MerchantProductUrl = browserResolved;
                offer.MerchantProductId = CimriMerchantProductIdExtractor.TryExtract(browserResolved);
                continue;
            }

            // 2) Capture edilmiş URL zaten cimri.com dışına yöneliyorsa onu doğrudan kullan.
            if (!IsCimriOfferRedirect(seedUrl))
            {
                offer.MerchantProductUrl = seedUrl;
                offer.MerchantProductId = CimriMerchantProductIdExtractor.TryExtract(seedUrl);
                continue;
            }

            // 3) Son çare: HttpClient resolver. Cimri çoğu zaman 403 dönüyor ama bazı satıcılar için işe yarayabilir.
            try
            {
                var resolved = await _offerUrlResolver.ResolveAsync(seedUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolved) && !IsCimriOfferRedirect(resolved))
                {
                    offer.MerchantProductUrl = resolved;
                    offer.MerchantProductId = CimriMerchantProductIdExtractor.TryExtract(resolved);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cimri offer redirect resolve edilemedi (idx={Idx}, url={Url})", i, seedUrl);
            }
        }
    }

    private static bool IsCimriOfferRedirect(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            return false;
        }

        if (!u.Host.EndsWith("cimri.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return u.AbsolutePath.StartsWith("/offer/", StringComparison.OrdinalIgnoreCase);
    }

    public static void ValidateProductUrl(string productUrl, CimriClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(productUrl))
        {
            throw new BusinessException("BeeBAK:CimriInvalidProductUrl");
        }

        if (!Uri.TryCreate(productUrl, UriKind.Absolute, out var uri))
        {
            throw new BusinessException("BeeBAK:CimriInvalidProductUrl")
                .WithData("ProductUrl", productUrl);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("BeeBAK:CimriProductUrlMustBeHttps")
                .WithData("ProductUrl", productUrl);
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new BusinessException("BeeBAK:CimriInvalidBaseUrl");
        }

        if (!string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("BeeBAK:CimriProductUrlHostNotAllowed")
                .WithData("Host", uri.Host)
                .WithData("ExpectedHost", baseUri.Host);
        }

        if (!CimriListingHtmlParser.TryExtractContentId(productUrl, out _))
        {
            throw new BusinessException("BeeBAK:CimriInvalidProductUrl")
                .WithData("ProductUrl", productUrl);
        }
    }
}
