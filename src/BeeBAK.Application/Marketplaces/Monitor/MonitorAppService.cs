using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Akakce;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Marketplaces.Cimri.Logging;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Monitor;

[Authorize(BeeBAKPermissions.Cimri.Sync)]
public class MonitorAppService : ApplicationService, IMonitorAppService
{
    private const int MaxEventsPerRun = 50;
    private const string ListingPhase  = "listing";

    private readonly IRepository<EcScrapeRun, Guid>      _runRepo;
    private readonly IRepository<EcScrapeRunEvent, Guid> _eventRepo;
    private readonly CimriTelegramPublishQueue            _cimriQueue;
    private readonly AkakceTelegramPublishQueue           _akakceQueue;
    private readonly TelegramSentHistory                  _sentHistory;

    public MonitorAppService(
        IRepository<EcScrapeRun, Guid>      runRepo,
        IRepository<EcScrapeRunEvent, Guid> eventRepo,
        CimriTelegramPublishQueue            cimriQueue,
        AkakceTelegramPublishQueue           akakceQueue,
        TelegramSentHistory                  sentHistory)
    {
        _runRepo     = runRepo;
        _eventRepo   = eventRepo;
        _cimriQueue  = cimriQueue;
        _akakceQueue = akakceQueue;
        _sentHistory = sentHistory;
    }

    public virtual async Task<AllActiveRunsDto> GetAllActiveAsync()
    {
        var cutoff    = DateTime.UtcNow.AddHours(-3);
        var queryable = await _runRepo.GetQueryableAsync();

        var allRuns = queryable
            .Where(r => r.Status == EcScrapeRunStatus.Running
                     || r.Status == EcScrapeRunStatus.Pending
                     || r.StartedUtc >= cutoff)
            .OrderByDescending(r => r.StartedUtc)
            .Take(30)
            .ToList();

        var cimriRuns  = new List<CimriListingSyncStatusDto>();
        var akakceRuns = new List<AkakceListingSyncStatusDto>();

        foreach (var run in allRuns.Where(r => r.Marketplace == MarketplaceKind.Cimri))
        {
            var events = await LoadCimriEventsAsync(run.Id);
            var meta   = await GetListingMetaAsync(run.Id);
            cimriRuns.Add(MapCimri(run, events, meta.url, meta.source));
        }

        foreach (var run in allRuns.Where(r => r.Marketplace == MarketplaceKind.Akakce))
        {
            var events = await LoadAkakceEventsAsync(run.Id);
            var meta   = await GetListingMetaAsync(run.Id);
            akakceRuns.Add(MapAkakce(run, events, meta.url, meta.source));
        }

        var recentSent = await _sentHistory.GetRecentAsync(20);

        var qCimri  = await _cimriQueue.GetSizeAsync();
        var qAkakce = await _akakceQueue.GetSizeAsync();

        return new AllActiveRunsDto
        {
            CimriRuns       = cimriRuns,
            AkakceRuns      = akakceRuns,
            RecentSent      = recentSent,
            TelegramQueueSize = qCimri + qAkakce,
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task<(string? url, string? source)> GetListingMetaAsync(Guid scrapeRunId)
    {
        var rows = await _eventRepo.GetListAsync(e =>
            e.ScrapeRunId == scrapeRunId && e.Phase == ListingPhase);
        var row = rows.OrderBy(e => e.TimestampUtc).FirstOrDefault();
        if (row == null) return (null, null);
        string? source = null;
        if (row.Message.StartsWith("[listingSource:form]",   StringComparison.OrdinalIgnoreCase)) source = "form";
        else if (row.Message.StartsWith("[listingSource:server]", StringComparison.OrdinalIgnoreCase)) source = "server";
        return (row.Url, source);
    }

    private async Task<List<CimriListingSyncEventDto>> LoadCimriEventsAsync(Guid runId)
    {
        var q = await _eventRepo.GetQueryableAsync();
        return q.Where(x => x.ScrapeRunId == runId)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(MaxEventsPerRun)
            .ToList()
            .OrderBy(x => x.TimestampUtc)
            .Select(e => new CimriListingSyncEventDto
            {
                Id = e.Id, TimestampUtc = e.TimestampUtc, Level = e.Level,
                Phase = e.Phase, Message = e.Message, Title = e.Title,
                Url = e.Url, Index = e.Index, Total = e.Total,
            })
            .ToList();
    }

    private async Task<List<AkakceListingSyncEventDto>> LoadAkakceEventsAsync(Guid runId)
    {
        var q = await _eventRepo.GetQueryableAsync();
        return q.Where(x => x.ScrapeRunId == runId)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(MaxEventsPerRun)
            .ToList()
            .OrderBy(x => x.TimestampUtc)
            .Select(e => new AkakceListingSyncEventDto
            {
                Id = e.Id, TimestampUtc = e.TimestampUtc, Level = e.Level,
                Phase = e.Phase, Message = e.Message, Title = e.Title,
                Url = e.Url, Index = e.Index, Total = e.Total,
            })
            .ToList();
    }

    private CimriListingSyncStatusDto MapCimri(
        EcScrapeRun run,
        List<CimriListingSyncEventDto> events,
        string? url, string? source)
    {
        var (elapsed, progress, eta, isActive) = CalcStats(run);
        return new CimriListingSyncStatusDto
        {
            ScrapeRunId              = run.Id,
            Status                   = run.Status,
            TotalItems               = run.TotalItems,
            ProcessedItems           = run.ProcessedItems,
            FailedItems              = run.FailedItems,
            CancelRequested          = run.CancelRequested,
            StartedUtc               = run.StartedUtc,
            CompletedUtc             = run.CompletedUtc,
            Notes                    = run.Notes,
            Progress                 = progress,
            ElapsedSeconds           = elapsed,
            EstimatedRemainingSeconds = eta,
            IsActive                 = isActive,
            Events                   = events,
            LatestEventUtc           = events.Count > 0 ? events[^1].TimestampUtc : null,
            ResolvedListingPageUrl   = url,
            ListingPageSource        = source,
        };
    }

    private AkakceListingSyncStatusDto MapAkakce(
        EcScrapeRun run,
        List<AkakceListingSyncEventDto> events,
        string? url, string? source)
    {
        var (elapsed, progress, eta, isActive) = CalcStats(run);
        return new AkakceListingSyncStatusDto
        {
            ScrapeRunId              = run.Id,
            Status                   = run.Status,
            TotalItems               = run.TotalItems,
            ProcessedItems           = run.ProcessedItems,
            FailedItems              = run.FailedItems,
            CancelRequested          = run.CancelRequested,
            StartedUtc               = run.StartedUtc,
            CompletedUtc             = run.CompletedUtc,
            Notes                    = run.Notes,
            Progress                 = progress,
            ElapsedSeconds           = elapsed,
            EstimatedRemainingSeconds = eta,
            IsActive                 = isActive,
            Events                   = events,
            LatestEventUtc           = events.Count > 0 ? events[^1].TimestampUtc : null,
            ResolvedListingPageUrl   = url,
            ListingPageSource        = source,
        };
    }

    private (double elapsed, double progress, double? eta, bool isActive) CalcStats(EcScrapeRun run)
    {
        var now       = Clock.Now;
        var endTime   = run.CompletedUtc ?? now;
        var elapsed   = Math.Max(0, (endTime - run.StartedUtc).TotalSeconds);
        var doneCount = run.ProcessedItems + run.FailedItems;
        var progress  = run.TotalItems > 0
            ? Math.Clamp(doneCount / (double)run.TotalItems, 0, 1)
            : 0;
        double? eta = null;
        if (run.Status == EcScrapeRunStatus.Running && run.TotalItems > 0 && doneCount > 0)
        {
            var perItem  = elapsed / doneCount;
            eta = perItem * Math.Max(0, run.TotalItems - doneCount);
        }
        var isActive = run.Status is EcScrapeRunStatus.Running or EcScrapeRunStatus.Pending;
        return (elapsed, progress, eta, isActive);
    }
}
