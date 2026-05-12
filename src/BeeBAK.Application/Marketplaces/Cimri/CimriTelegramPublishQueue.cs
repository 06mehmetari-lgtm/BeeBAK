using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Medallion.Threading;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Redis üzerinde çalışan öncelik sıralı Telegram yayın kuyruğu.
/// Tüm okuma/yazma işlemleri distributed lock ile korunur.
/// </summary>
public class CimriTelegramPublishQueue : ITransientDependency
{
    private const string QueueKey  = "beebak:tg:queue:v2";
    private const string LockKey   = "cimri:tg:queue:lock";
    private const int    MaxSize   = 500;

    private readonly IDistributedCache _cache;
    private readonly IDistributedLockProvider? _lockProvider;

    public CimriTelegramPublishQueue(
        IDistributedCache cache,
        IDistributedLockProvider? lockProvider = null)
    {
        _cache        = cache;
        _lockProvider = lockProvider;
    }

    // ── Kuyruğa ekle / güncelle ──────────────────────────────────────────
    public async Task EnqueueAsync(CimriPublishQueueEntry entry, CancellationToken ct = default)
    {
        await WithLockAsync(async () =>
        {
            var list = await ReadAsync(ct);
            list.RemoveAll(e => e.ContentId == entry.ContentId);
            list.Add(entry);

            // En yüksek skorluları tut
            if (list.Count > MaxSize)
                list = list.OrderByDescending(e => e.Score).Take(MaxSize).ToList();

            await WriteAsync(list, ct);
        }, ct);
    }

    // ── En yüksek skorlu ürünü al ve kuyruktan çıkar ──────────────────────
    /// <param name="avoidMerchant">Bu mağaza adından farklı bir ürün tercih edilir.</param>
    public async Task<CimriPublishQueueEntry?> DequeueTopAsync(
        string? avoidMerchant = null, CancellationToken ct = default)
    {
        CimriPublishQueueEntry? top = null;

        await WithLockAsync(async () =>
        {
            var list = await ReadAsync(ct);
            if (list.Count == 0) return;

            var sorted = list.OrderByDescending(e => e.Score).ToList();

            // Top 10'dan rastgele seç — sürekli aynı ürünü engeller
            var pool = sorted.Take(Math.Min(10, sorted.Count)).ToList();

            // Son mağazayı hariç tut (mümkünse)
            var candidates = !string.IsNullOrEmpty(avoidMerchant)
                ? pool.Where(e => !string.Equals(e.MerchantName, avoidMerchant,
                    StringComparison.OrdinalIgnoreCase)).ToList()
                : pool;

            if (candidates.Count == 0) candidates = pool; // tüm kuyruk aynı mağazaysa fallback

            top = candidates[Random.Shared.Next(candidates.Count)];
            list.Remove(top);
            await WriteAsync(list, ct);
        }, ct);

        return top;
    }

    // ── Kuyruğu boşalt (iptal/reset) ─────────────────────────────────────
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await WithLockAsync(async () =>
        {
            await _cache.RemoveAsync(QueueKey, ct);
        }, ct);
    }

    // ── Kuyruk boyutu ─────────────────────────────────────────────────────
    public async Task<int> GetSizeAsync(CancellationToken ct = default)
    {
        var list = await ReadAsync(ct);   // lock gerekmez, okuma atomik
        return list.Count;
    }

    // ── İç yardımcılar ───────────────────────────────────────────────────
    private async Task<List<CimriPublishQueueEntry>> ReadAsync(CancellationToken ct)
    {
        var json = await _cache.GetStringAsync(QueueKey, ct);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<CimriPublishQueueEntry>>(json) ?? []; }
        catch { return []; }
    }

    private Task WriteAsync(List<CimriPublishQueueEntry> list, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(list);
        return _cache.SetStringAsync(QueueKey, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(48),
        }, ct);
    }

    private async Task WithLockAsync(Func<Task> action, CancellationToken ct)
    {
        if (_lockProvider != null)
        {
            // 5 saniye bekle — kuyruk işlemi kısa, bu süre yeterli
            await using var handle = await _lockProvider.AcquireLockAsync(LockKey, TimeSpan.FromSeconds(5), ct);
            await action();
        }
        else
        {
            await action();
        }
    }
}
