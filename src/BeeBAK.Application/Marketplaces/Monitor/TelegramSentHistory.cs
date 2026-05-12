using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using BeeBAK.Marketplaces;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Monitor;

/// <summary>
/// Redis'te son gönderilen Telegram ürünlerini tutan küçük geçmiş tamponu.
/// Publisher worker'lar her başarılı gönderi sonrası buraya yazar;
/// canlı izleme sayfası buradan okur.
/// </summary>
public class TelegramSentHistory : ITransientDependency
{
    private const string HistoryKey = "beebak:tg:sent:recent";
    private const int MaxItems = 50;

    private readonly IDistributedCache _cache;

    public TelegramSentHistory(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task AddAsync(TelegramSentItemDto item)
    {
        try
        {
            var list = await ReadAsync();
            // En başa ekle (yeni → eski sırası)
            list.Insert(0, item);
            if (list.Count > MaxItems)
                list = list.GetRange(0, MaxItems);
            await WriteAsync(list);
        }
        catch
        {
            // best-effort — gönderim başarısı geçmişe yazma hatasından etkilenmemeli
        }
    }

    public async Task<List<TelegramSentItemDto>> GetRecentAsync(int count = 20)
    {
        try
        {
            var list = await ReadAsync();
            return list.Count <= count ? list : list.GetRange(0, count);
        }
        catch
        {
            return new List<TelegramSentItemDto>();
        }
    }

    private async Task<List<TelegramSentItemDto>> ReadAsync()
    {
        var json = await _cache.GetStringAsync(HistoryKey);
        if (string.IsNullOrEmpty(json)) return new List<TelegramSentItemDto>();
        try { return JsonSerializer.Deserialize<List<TelegramSentItemDto>>(json) ?? new(); }
        catch { return new List<TelegramSentItemDto>(); }
    }

    private Task WriteAsync(List<TelegramSentItemDto> list)
    {
        var json = JsonSerializer.Serialize(list);
        return _cache.SetStringAsync(HistoryKey, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(48),
        });
    }
}
