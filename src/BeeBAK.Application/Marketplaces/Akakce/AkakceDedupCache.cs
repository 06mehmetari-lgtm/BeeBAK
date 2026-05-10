using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Akakce;

public interface IAkakceDedupCache
{
    Task<bool> TryAcquireAsync(string productCode, TimeSpan? ttl = null, CancellationToken cancellationToken = default);
    Task<bool> IsRecentlyVisitedAsync(string productCode, CancellationToken cancellationToken = default);
}

public class AkakceDedupCache : IAkakceDedupCache, ITransientDependency
{
    private const string KeyPrefix = "akakce:visited:";

    private readonly IDistributedCache _cache;
    private readonly IOptionsMonitor<AkakceClientOptions> _options;
    private readonly ILogger<AkakceDedupCache> _logger;

    public AkakceDedupCache(
        IDistributedCache cache,
        IOptionsMonitor<AkakceClientOptions> options,
        ILogger<AkakceDedupCache> logger)
    {
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(string productCode, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return true;
        }

        try
        {
            var key = KeyPrefix + productCode;
            var existing = await _cache.GetAsync(key, cancellationToken);
            if (existing is { Length: > 0 })
            {
                return false;
            }

            await _cache.SetAsync(
                key,
                new[] { (byte)1 },
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromSeconds(Math.Max(60, _options.CurrentValue.DedupTtlSeconds)),
                },
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Akakce dedup cache unavailable ({ProductCode})", productCode);
            return true;
        }
    }

    public async Task<bool> IsRecentlyVisitedAsync(string productCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return false;
        }

        try
        {
            var existing = await _cache.GetAsync(KeyPrefix + productCode, cancellationToken);
            return existing is { Length: > 0 };
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Akakce dedup read failed ({ProductCode})", productCode);
            return false;
        }
    }
}
