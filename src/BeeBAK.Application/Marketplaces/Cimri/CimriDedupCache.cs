using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Aynı Cimri ürünü kısa süre içinde tekrar tekrar scrape edilmesin diye Redis (IDistributedCache)
/// üzerinden dedup yapan ince servis. Cache yoksa (in-memory fallback) sessizce devre dışıdır.
/// </summary>
public interface ICimriDedupCache
{
    Task<bool> TryAcquireAsync(string contentId, TimeSpan? ttl = null, CancellationToken cancellationToken = default);
    Task ReleaseAsync(string contentId, CancellationToken cancellationToken = default);
    Task<bool> IsRecentlyVisitedAsync(string contentId, CancellationToken cancellationToken = default);
}

public class CimriDedupCache : ICimriDedupCache, ITransientDependency
{
    private const string KeyPrefix = "cimri:visited:";

    private readonly IDistributedCache _cache;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly ILogger<CimriDedupCache> _logger;

    public CimriDedupCache(
        IDistributedCache cache,
        IOptionsMonitor<CimriClientOptions> options,
        ILogger<CimriDedupCache> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(string contentId, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return true;
        }

        try
        {
            var key = KeyPrefix + contentId;
            var existing = await _cache.GetAsync(key, cancellationToken);
            if (existing != null && existing.Length > 0)
            {
                return false;
            }

            var lifetime = ttl ?? TimeSpan.FromSeconds(Math.Max(60, _options.CurrentValue.DedupTtlSeconds));
            await _cache.SetAsync(
                key,
                new[] { (byte)1 },
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = lifetime,
                },
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cimri dedup cache erişilemedi (contentId={ContentId})", contentId);
            return true;
        }
    }

    public async Task ReleaseAsync(string contentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return;
        }

        try
        {
            await _cache.RemoveAsync(KeyPrefix + contentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri dedup release başarısız: {ContentId}", contentId);
        }
    }

    public async Task<bool> IsRecentlyVisitedAsync(string contentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return false;
        }

        try
        {
            var existing = await _cache.GetAsync(KeyPrefix + contentId, cancellationToken);
            return existing != null && existing.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Cimri dedup okuma başarısız: {ContentId}", contentId);
            return false;
        }
    }
}
