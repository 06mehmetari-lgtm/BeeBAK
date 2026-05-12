using System.Collections.Generic;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Marketplaces.Akakce;

namespace BeeBAK.Marketplaces.Monitor;

/// <summary>
/// Canlı izleme sayfası için tek seferde tüm aktif/son tarama çalışmaları
/// ve son Telegram paylaşımları.
/// </summary>
public class AllActiveRunsDto
{
    /// <summary>Son 3 saatteki tüm Cimri ScrapeRun'ları (Running önce).</summary>
    public List<CimriListingSyncStatusDto> CimriRuns { get; set; } = new();

    /// <summary>Son 3 saatteki tüm Akakçe ScrapeRun'ları (Running önce).</summary>
    public List<AkakceListingSyncStatusDto> AkakceRuns { get; set; } = new();

    /// <summary>Son gönderilen ürünler (Telegram kartları).</summary>
    public List<TelegramSentItemDto> RecentSent { get; set; } = new();

    /// <summary>Bekleyen Telegram kuyruğu boyutu.</summary>
    public int TelegramQueueSize { get; set; }
}
