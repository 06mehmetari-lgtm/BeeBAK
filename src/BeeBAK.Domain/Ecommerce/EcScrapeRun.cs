using System;
using BeeBAK.Marketplaces;
using Volo.Abp.Domain.Entities.Auditing;

namespace BeeBAK.Ecommerce;

/// <summary>Toplu çekim / senkron işinin iz kaydı.</summary>
public class EcScrapeRun : FullAuditedAggregateRoot<Guid>
{
    public MarketplaceKind Marketplace { get; protected set; }

    public EcScrapeRunStatus Status { get; protected set; }

    public DateTime StartedUtc { get; protected set; }

    public DateTime? CompletedUtc { get; protected set; }

    public string? TriggerSource { get; protected set; }

    public string? Notes { get; protected set; }

    /// <summary>Sayılan ürün, hata vb.</summary>
    public string? StatisticsJson { get; protected set; }

    /// <summary>Discovery aşamasında planlanan toplam ürün sayısı (listing parser sonrası set edilir).</summary>
    public int TotalItems { get; protected set; }

    /// <summary>Detail job'ları başarıyla işlediği ürün sayısı.</summary>
    public int ProcessedItems { get; protected set; }

    /// <summary>Detail job'larında patlamış ürün sayısı.</summary>
    public int FailedItems { get; protected set; }

    /// <summary>Kullanıcı iptal istedi — discovery + detail job'ları kontrol eder ve nazikçe çıkar.</summary>
    public bool CancelRequested { get; protected set; }

    protected EcScrapeRun()
    {
    }

    public EcScrapeRun(
        Guid id,
        MarketplaceKind marketplace,
        EcScrapeRunStatus status,
        DateTime startedUtc,
        string? triggerSource = null)
        : base(id)
    {
        Marketplace = marketplace;
        Status = status;
        StartedUtc = startedUtc;
        TriggerSource = triggerSource;
    }

    public void SetTotalItems(int total)
    {
        TotalItems = Math.Max(0, total);
    }

    public void IncrementProcessed()
    {
        ProcessedItems += 1;
    }

    public void IncrementFailed()
    {
        FailedItems += 1;
    }

    public void RequestCancel()
    {
        CancelRequested = true;
    }

    public void MarkCancelled(DateTime utcNow, string? notes = null)
    {
        Status = EcScrapeRunStatus.Cancelled;
        CompletedUtc = utcNow;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes;
        }
    }

    public void Complete(DateTime utcNow, string? statisticsJson = null)
    {
        Status = EcScrapeRunStatus.Completed;
        CompletedUtc = utcNow;
        StatisticsJson = statisticsJson;
    }

    public void Fail(DateTime utcNow, string? notes = null)
    {
        Status = EcScrapeRunStatus.Failed;
        CompletedUtc = utcNow;
        Notes = notes;
    }
}
