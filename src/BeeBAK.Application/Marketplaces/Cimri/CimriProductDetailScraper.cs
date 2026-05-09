using System;
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
    private readonly IClock _clock;
    private readonly ILogger<CimriProductDetailScraper> _logger;

    public CimriProductDetailScraper(
        CimriSeleniumPageFetcher seleniumPageFetcher,
        IClock clock,
        ILogger<CimriProductDetailScraper> logger)
    {
        _seleniumPageFetcher = seleniumPageFetcher;
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

        var html = await _seleniumPageFetcher.TryGetProductDetailHtmlAsync(
            productUrl,
            expandAllOffers,
            options,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(html))
        {
            _logger.LogWarning("Cimri PDP boş HTML döndü: {ProductUrl}", productUrl);
            return null;
        }

        return CimriProductDetailHtmlParser.TryParse(html, productUrl, _clock.Now);
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
