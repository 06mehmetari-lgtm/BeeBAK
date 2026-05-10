using System;
using System.Collections.Generic;
using BeeBAK.Ecommerce;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceListingSyncStatusDto
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
    public double Progress { get; set; }
    public double ElapsedSeconds { get; set; }
    public double? EstimatedRemainingSeconds { get; set; }
    public bool IsActive { get; set; }
    public List<AkakceListingSyncEventDto> Events { get; set; } = new();
    public DateTime? LatestEventUtc { get; set; }
    public string? ResolvedListingPageUrl { get; set; }
    public string? ListingPageSource { get; set; }
}
