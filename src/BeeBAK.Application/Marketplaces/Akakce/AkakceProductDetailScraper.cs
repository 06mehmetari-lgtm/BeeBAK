using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceProductDetailScraper : ITransientDependency
{
    private readonly AkakceSeleniumPageFetcher _seleniumPageFetcher;
    private readonly IClock _clock;
    private readonly ILogger<AkakceProductDetailScraper> _logger;

    public AkakceProductDetailScraper(
        AkakceSeleniumPageFetcher seleniumPageFetcher,
        IClock clock,
        ILogger<AkakceProductDetailScraper> logger)
    {
        _seleniumPageFetcher = seleniumPageFetcher;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AkakceProductDetailExtract?> FetchAsync(
        string productCode,
        string productUrl,
        AkakceClientOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateProductUrl(productUrl, options);

        var html = await _seleniumPageFetcher.TryGetProductDetailHtmlAsync(productUrl, options, cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
        {
            _logger.LogWarning("Akakce PDP empty HTML: {ProductUrl}", productUrl);
            return null;
        }

        return AkakceProductDetailHtmlParser.TryParse(html, productUrl, productCode, _clock.Now);
    }

    public static void ValidateProductUrl(string productUrl, AkakceClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(productUrl)
            || !Uri.TryCreate(productUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new BusinessException("BeeBAK:AkakceInvalidProductUrl")
                .WithData("ProductUrl", productUrl);
        }

        if (!AkakceCrawlHost.IsAllowedHost(uri, options))
        {
            throw new BusinessException("BeeBAK:AkakceProductUrlHostNotAllowed")
                .WithData("Host", uri.Host);
        }
    }
}
