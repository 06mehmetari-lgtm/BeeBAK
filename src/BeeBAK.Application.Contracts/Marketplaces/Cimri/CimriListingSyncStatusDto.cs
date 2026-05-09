using System;
using System.Collections.Generic;
using BeeBAK.Ecommerce;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Tek bir Cimri scrape çalışmasının canlı ilerleme görünümü. UI bu DTO'yu periyodik (~2 sn) sorgulayarak
/// progress bar / ETA / iptal butonu yönetir.
/// </summary>
public class CimriListingSyncStatusDto
{
    public Guid ScrapeRunId { get; set; }

    public EcScrapeRunStatus Status { get; set; }

    public int TotalItems { get; set; }

    public int ProcessedItems { get; set; }

    public int FailedItems { get; set; }

    public bool CancelRequested { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public string? Notes { get; set; }

    /// <summary>0..1 aralığında ilerleme oranı (TotalItems sıfırsa 0).</summary>
    public double Progress { get; set; }

    /// <summary>Şu ana kadar geçen süre (saniye).</summary>
    public double ElapsedSeconds { get; set; }

    /// <summary>Tahmini kalan süre (saniye). TotalItems veya ProcessedItems sıfırsa null.</summary>
    public double? EstimatedRemainingSeconds { get; set; }

    /// <summary>True iken UI polling'i devam ettirmeli (Running veya iptal beklemede).</summary>
    public bool IsActive { get; set; }

    /// <summary>Konsol gibi akan canlı log paneli için son N olay (eski → yeni sıralı).</summary>
    public List<CimriListingSyncEventDto> Events { get; set; } = new();

    /// <summary>UI delta polling için: bu değerden büyük TimestampUtc'leri çekmesi yeterli.</summary>
    public DateTime? LatestEventUtc { get; set; }
}
