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
