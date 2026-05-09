using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri'nin <c>https://www.cimri.com/offer/{id}?...</c> redirect URL'leri ile çözülmüş gerçek mağaza
/// URL'leri arasındaki haritayı Redis'te tutar. Aynı offer farklı ürün listelerinde tekrarlanırsa
/// Selenium yeni-tab adımı atlanıp anlık (~0 ms) sonuç döner. Cache yoksa sessizce devre dışıdır.
/// </summary>
public interface ICimriOfferUrlCache
{
    /// <summary>Cache'te varsa final mağaza URL'sini döner; yoksa null.</summary>
    Task<string?> TryGetAsync(string cimriOfferUrl, CancellationToken cancellationToken = default);

    /// <summary>Çözülmüş final mağaza URL'sini cache'e yazar (TTL = <see cref="CimriClientOptions.OfferUrlCacheTtlSeconds"/>).</summary>
    Task SetAsync(string cimriOfferUrl, string finalMerchantUrl, CancellationToken cancellationToken = default);
}

public class CimriOfferUrlCache : ICimriOfferUrlCache, ITransientDependency
{
    private const string KeyPrefix = "cimri:offer-url:";

    private readonly IDistributedCache _cache;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly ILogger<CimriOfferUrlCache> _logger;

    public CimriOfferUrlCache(
        IDistributedCache cache,
        IOptionsMonitor<CimriClientOptions> options,
        ILogger<CimriOfferUrlCache> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> TryGetAsync(string cimriOfferUrl, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(cimriOfferUrl);
        if (key == null)
        {
            return null;
        }

        try
        {
            var bytes = await _cache.GetAsync(key, cancellationToken);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri offer-url cache okuma başarısız ({Url})", cimriOfferUrl);
            return null;
        }
    }

    public async Task SetAsync(string cimriOfferUrl, string finalMerchantUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(finalMerchantUrl))
        {
            return;
        }

        var key = BuildCacheKey(cimriOfferUrl);
        if (key == null)
        {
            return;
        }

        try
        {
            var ttlSeconds = Math.Max(60, _options.CurrentValue.OfferUrlCacheTtlSeconds);
            await _cache.SetAsync(
                key,
                Encoding.UTF8.GetBytes(finalMerchantUrl),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttlSeconds),
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri offer-url cache yazma başarısız ({Url})", cimriOfferUrl);
        }
    }

    /// <summary>
    /// Cimri offer redirect URL'sinden cache anahtarı çıkarır. Sadece <c>/offer/{id}</c> path bölümü
    /// kullanılır; query string (productId, offerId vb.) anahtarda yer almaz çünkü aynı offer kimliği
    /// her zaman aynı mağaza URL'sine çözülür.
    /// </summary>
    private string? BuildCacheKey(string cimriOfferUrl)
    {
        if (string.IsNullOrWhiteSpace(cimriOfferUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(cimriOfferUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!CimriCrawlHost.IsAllowedHost(uri, _options.CurrentValue))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        if (!path.StartsWith("/offer/", StringComparison.OrdinalIgnoreCase) || path.Length <= "/offer/".Length)
        {
            return null;
        }

        var offerId = path["/offer/".Length..].TrimEnd('/');
        if (string.IsNullOrEmpty(offerId))
        {
            return null;
        }

        return KeyPrefix + offerId;
    }
}
