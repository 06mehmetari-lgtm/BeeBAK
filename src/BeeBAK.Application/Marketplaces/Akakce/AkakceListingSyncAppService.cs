using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Akakce.Jobs;
using BeeBAK.Marketplaces.Cimri.Logging;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Akakce;

[Authorize(BeeBAKPermissions.Akakce.Sync)]
public class AkakceListingSyncAppService : ApplicationService, IAkakceListingSyncAppService
{
    private const int MaxEventsPerStatus = 200;
    private const string ListingTargetPhase = "listing";

    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IRepository<EcScrapeRunEvent, Guid> _eventRepository;
    private readonly IOptionsMonitor<AkakceClientOptions> _options;
    private readonly AkakceSeleniumPageFetcher _seleniumPageFetcher;
    private readonly AkakceProductIngestionService _ingestionService;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IScrapeRunEventLogger _eventLogger;
    private readonly ILogger<AkakceListingSyncAppService> _logger;

    public AkakceListingSyncAppService(
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IRepository<EcScrapeRunEvent, Guid> eventRepository,
        IOptionsMonitor<AkakceClientOptions> options,
        AkakceSeleniumPageFetcher seleniumPageFetcher,
        AkakceProductIngestionService ingestionService,
        IBackgroundJobManager backgroundJobManager,
        IScrapeRunEventLogger eventLogger,
        ILogger<AkakceListingSyncAppService> logger)
    {
        _scrapeRunRepository = scrapeRunRepository;
        _eventRepository = eventRepository;
        _options = options;
        _seleniumPageFetcher = seleniumPageFetcher;
        _ingestionService = ingestionService;
        _backgroundJobManager = backgroundJobManager;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    public virtual async Task<AkakceListingSyncResultDto> SyncAsync(AkakceListingSyncInput? input)
    {
        var effectiveInput = input ?? new AkakceListingSyncInput();
        var options = _options.CurrentValue;
        var maxPages = NormalizePositive(effectiveInput.MaxPages, options.DefaultMaxPages, 1, 200);
        var maxProducts = NormalizePositive(effectiveInput.MaxProducts, options.DefaultMaxProducts, 1, 5000);
        var includeOffers = effectiveInput.IncludeProductDetails ?? effectiveInput.IncludeOffers;
        var listingUrl = AkakceListingUrlResolver.Resolve(options, effectiveInput.ListingPageUrl);

        var scrapeRun = new EcScrapeRun(
            GuidGenerator.Create(),
            MarketplaceKind.Akakce,
            EcScrapeRunStatus.Running,
            Clock.Now,
            triggerSource: nameof(AkakceListingSyncAppService));
        await _scrapeRunRepository.InsertAsync(scrapeRun, autoSave: true);

        var listingFromForm = !string.IsNullOrWhiteSpace(effectiveInput.ListingPageUrl?.Trim());
        var listingSourceCode = listingFromForm ? "form" : "server";
        await _eventLogger.LogAsync(
            scrapeRun.Id, EcScrapeRunEventLevel.Info, ListingTargetPhase,
            $"[listingSource:{listingSourceCode}] Akakce listing URL resolved.",
            url: listingUrl);

        await _eventLogger.LogAsync(
            scrapeRun.Id, EcScrapeRunEventLevel.Info, "system",
            $"Sync started (pages: {maxPages}, max products: {maxProducts}).");

        if (options.UseQueue)
        {
            await _backgroundJobManager.EnqueueAsync(new AkakceListingDiscoveryJobArgs
            {
                ScrapeRunId = scrapeRun.Id,
                MaxPages = maxPages,
                MaxProducts = maxProducts,
                IncludeOffers = includeOffers,
                ForceRefresh = effectiveInput.ForceRefresh,
                ListingPageUrl = listingUrl,
            });

            await _eventLogger.LogAsync(scrapeRun.Id, EcScrapeRunEventLevel.Info, "system", "Discovery job queued; waiting for worker pickup.");
            return new AkakceListingSyncResultDto
            {
                ScrapeRunId = scrapeRun.Id,
                ResolvedListingPageUrl = listingUrl,
                Queued = true,
            };
        }

        var productsAffected = 0;
        var offersAffected = 0;
        var merchantsTouched = new HashSet<Guid>();
        var pagesFetched = 0;

        try
        {
            var cardsByCode = new Dictionary<string, AkakceListingCard>(StringComparer.Ordinal);
            for (var page = 1; page <= maxPages && cardsByCode.Count < maxProducts; page++)
            {
                var pageUrl = AkakceListingUrlResolver.BuildPageUrl(listingUrl, page);
                var html = await _seleniumPageFetcher.TryGetListingHtmlAsync(pageUrl, options);
                if (string.IsNullOrWhiteSpace(html))
                {
                    throw new Volo.Abp.BusinessException("BeeBAK:AkakceListingHtmlEmpty").WithData("ListingUrl", pageUrl);
                }

                foreach (var card in AkakceListingHtmlParser.Parse(html, options.BaseUrl))
                {
                    if (cardsByCode.Count >= maxProducts) break;
                    cardsByCode.TryAdd(card.ProductCode, card);
                }

                pagesFetched++;
            }

            foreach (var card in AkakceListingCardSorter.SortByDiscountDescending(cardsByCode.Values).Take(maxProducts))
            {
                try
                {
                    var result = await _ingestionService.UpsertAsync(card, includeOffers);
                    productsAffected++;
                    offersAffected += result.OffersAdded;
                    foreach (var merchantId in result.TouchedMerchantIds)
                    {
                        merchantsTouched.Add(merchantId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Akakce inline product sync failed: {ProductCode}", card.ProductCode);
                }
            }

            scrapeRun.Complete(Clock.Now);
            await _scrapeRunRepository.UpdateAsync(scrapeRun, autoSave: true);
        }
        catch (Exception ex)
        {
            scrapeRun.Fail(Clock.Now, ex.Message);
            await _scrapeRunRepository.UpdateAsync(scrapeRun, autoSave: true);
            throw;
        }

        return new AkakceListingSyncResultDto
        {
            ScrapeRunId = scrapeRun.Id,
            PagesFetched = pagesFetched,
            ProductsAffected = productsAffected,
            OffersAffected = offersAffected,
            MerchantsAffected = merchantsTouched.Count,
            ResolvedListingPageUrl = listingUrl,
        };
    }

    public virtual async Task<AkakceListingSyncStatusDto> GetStatusAsync(Guid scrapeRunId, DateTime? sinceUtc = null)
    {
        var run = await _scrapeRunRepository.FindAsync(scrapeRunId)
                  ?? throw new EntityNotFoundException(typeof(EcScrapeRun), scrapeRunId);
        var events = await LoadEventsAsync(scrapeRunId, sinceUtc);
        var listingMeta = await GetListingTargetMetaAsync(scrapeRunId);
        return MapToStatus(run, events, listingMeta.url, listingMeta.source);
    }

    public virtual async Task<AkakceListingSyncStatusDto> CancelAsync(Guid scrapeRunId)
    {
        var run = await _scrapeRunRepository.FindAsync(scrapeRunId)
                  ?? throw new EntityNotFoundException(typeof(EcScrapeRun), scrapeRunId);

        if (run.Status is EcScrapeRunStatus.Completed or EcScrapeRunStatus.Failed or EcScrapeRunStatus.Cancelled)
        {
            var events = await LoadEventsAsync(scrapeRunId, null);
            var listingMeta = await GetListingTargetMetaAsync(scrapeRunId);
            return MapToStatus(run, events, listingMeta.url, listingMeta.source);
        }

        run.RequestCancel();
        run.MarkCancelled(Clock.Now);
        await _scrapeRunRepository.UpdateAsync(run, autoSave: true);
        await _eventLogger.LogAsync(scrapeRunId, EcScrapeRunEventLevel.Warning, "system", "User requested cancellation.");

        var afterEvents = await LoadEventsAsync(scrapeRunId, null);
        var listingMetaFinal = await GetListingTargetMetaAsync(scrapeRunId);
        return MapToStatus(run, afterEvents, listingMetaFinal.url, listingMetaFinal.source);
    }

    private async Task<(string? url, string? source)> GetListingTargetMetaAsync(Guid scrapeRunId)
    {
        var rows = await _eventRepository.GetListAsync(e => e.ScrapeRunId == scrapeRunId && e.Phase == ListingTargetPhase);
        var row = rows.OrderBy(e => e.TimestampUtc).FirstOrDefault();
        if (row == null) return (null, null);
        string? source = null;
        if (row.Message.StartsWith("[listingSource:form]", StringComparison.OrdinalIgnoreCase)) source = "form";
        else if (row.Message.StartsWith("[listingSource:server]", StringComparison.OrdinalIgnoreCase)) source = "server";
        return (row.Url, source);
    }

    private async Task<List<AkakceListingSyncEventDto>> LoadEventsAsync(Guid scrapeRunId, DateTime? sinceUtc)
    {
        var queryable = await _eventRepository.GetQueryableAsync();
        IQueryable<EcScrapeRunEvent> q = queryable.Where(x => x.ScrapeRunId == scrapeRunId);
        if (sinceUtc.HasValue) q = q.Where(x => x.TimestampUtc > sinceUtc.Value);

        return q.OrderByDescending(x => x.TimestampUtc)
            .Take(MaxEventsPerStatus)
            .ToList()
            .OrderBy(x => x.TimestampUtc)
            .Select(e => new AkakceListingSyncEventDto
            {
                Id = e.Id,
                TimestampUtc = e.TimestampUtc,
                Level = e.Level,
                Phase = e.Phase,
                Message = e.Message,
                Title = e.Title,
                Url = e.Url,
                Index = e.Index,
                Total = e.Total,
            })
            .ToList();
    }

    private AkakceListingSyncStatusDto MapToStatus(
        EcScrapeRun run,
        List<AkakceListingSyncEventDto> events,
        string? resolvedListingPageUrl = null,
        string? listingPageSource = null)
    {
        var now = Clock.Now;
        var endTime = run.CompletedUtc ?? now;
        var elapsedSeconds = Math.Max(0, (endTime - run.StartedUtc).TotalSeconds);
        var doneCount = run.ProcessedItems + run.FailedItems;
        var progress = run.TotalItems > 0 ? Math.Clamp(doneCount / (double)run.TotalItems, 0, 1) : 0;
        double? estimatedRemaining = null;
        if (run.Status == EcScrapeRunStatus.Running && run.TotalItems > 0 && doneCount > 0)
        {
            var perItem = elapsedSeconds / doneCount;
            estimatedRemaining = perItem * Math.Max(0, run.TotalItems - doneCount);
        }

        var isActive = run.Status == EcScrapeRunStatus.Running || run.Status == EcScrapeRunStatus.Pending;
        return new AkakceListingSyncStatusDto
        {
            ScrapeRunId = run.Id,
            Status = run.Status,
            TotalItems = run.TotalItems,
            ProcessedItems = run.ProcessedItems,
            FailedItems = run.FailedItems,
            CancelRequested = run.CancelRequested,
            StartedUtc = run.StartedUtc,
            CompletedUtc = run.CompletedUtc,
            Notes = run.Notes,
            Progress = progress,
            ElapsedSeconds = elapsedSeconds,
            EstimatedRemainingSeconds = estimatedRemaining,
            IsActive = isActive,
            Events = events,
            LatestEventUtc = events.Count > 0 ? events[^1].TimestampUtc : (DateTime?)null,
            ResolvedListingPageUrl = resolvedListingPageUrl,
            ListingPageSource = listingPageSource,
        };
    }

    private static int NormalizePositive(int? input, int defaultValue, int min, int max)
    {
        if (input is null || input.Value < min) return Math.Clamp(defaultValue, min, max);
        return Math.Clamp(input.Value, min, max);
    }
}
