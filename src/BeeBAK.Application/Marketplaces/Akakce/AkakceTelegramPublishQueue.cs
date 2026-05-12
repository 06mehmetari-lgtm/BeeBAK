using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Threading;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceTelegramPublishQueue : ITransientDependency
{
    private const string QueueKey = "beebak:tg:akakce:queue:v1";
    private const string LockKey  = "akakce:tg:queue:lock";
    private const int    MaxSize  = 300;

    private readonly IDistributedCache _cache;
    private readonly IDistributedLockProvider? _lockProvider;

    public AkakceTelegramPublishQueue(
        IDistributedCache cache,
        IDistributedLockProvider? lockProvider = null)
    {
        _cache        = cache;
        _lockProvider = lockProvider;
    }

    public async Task EnqueueAsync(AkakcePublishQueueEntry entry, CancellationToken ct = default)
    {
        await WithLockAsync(async () =>
        {
            var list = await ReadAsync(ct);
            list.RemoveAll(e => e.ProductCode == entry.ProductCode);
            list.Add(entry);
            if (list.Count > MaxSize)
                list = list.OrderByDescending(e => e.Score).Take(MaxSize).ToList();
            await WriteAsync(list, ct);
        }, ct);
    }

    /// <param name="avoidMerchant">Bu mağaza adından farklı bir ürün tercih edilir.
    /// Kuyrukta sadece bu mağazadan ürün varsa yine de o alınır (zorunlu fallback).</param>
    public async Task<AkakcePublishQueueEntry?> DequeueTopAsync(
        string? avoidMerchant = null, CancellationToken ct = default)
    {
        AkakcePublishQueueEntry? top = null;
        await WithLockAsync(async () =>
        {
            var list = await ReadAsync(ct);
            if (list.Count == 0) return;

            var sorted = list.OrderByDescending(e => e.Score).ToList();

            if (!string.IsNullOrEmpty(avoidMerchant) && sorted.Count > 1)
            {
                top = sorted.FirstOrDefault(e =>
                    !string.Equals(e.MerchantName, avoidMerchant,
                        StringComparison.OrdinalIgnoreCase))
                    ?? sorted[0];
            }
            else
            {
                top = sorted[0];
            }

            list.Remove(top);
            await WriteAsync(list, ct);
        }, ct);
        return top;
    }

    public async Task<int> GetSizeAsync(CancellationToken ct = default)
    {
        var list = await ReadAsync(ct);
        return list.Count;
    }

    private async Task<List<AkakcePublishQueueEntry>> ReadAsync(CancellationToken ct)
    {
        var json = await _cache.GetStringAsync(QueueKey, ct);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<AkakcePublishQueueEntry>>(json) ?? []; }
        catch { return []; }
    }

    private Task WriteAsync(List<AkakcePublishQueueEntry> list, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(list);
        return _cache.SetStringAsync(QueueKey, json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(48) }, ct);
    }

    private async Task WithLockAsync(Func<Task> action, CancellationToken ct)
    {
        if (_lockProvider != null)
        {
            await using var handle = await _lockProvider.AcquireLockAsync(LockKey, TimeSpan.FromSeconds(5), ct);
            await action();
        }
        else
        {
            await action();
        }
    }
}
