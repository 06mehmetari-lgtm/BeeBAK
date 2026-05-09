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

        await PopulateMerchantUrlsAsync(extract, fetchResult.CapturedOfferUrls, cancellationToken);
        return extract;
    }

    private async Task PopulateMerchantUrlsAsync(
        CimriProductDetailExtract extract,
        IReadOnlyList<string> capturedOfferUrls,
        CancellationToken cancellationToken)
    {
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

            try
            {
                var resolved = await _offerUrlResolver.ResolveAsync(seedUrl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolved))
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
