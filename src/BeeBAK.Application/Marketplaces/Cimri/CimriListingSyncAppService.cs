using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri.Jobs;
using BeeBAK.Marketplaces.Cimri.Logging;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Cimri;

[Authorize(BeeBAKPermissions.Cimri.Sync)]
public class CimriListingSyncAppService : ApplicationService, ICimriListingSyncAppService
{
    private const int MaxEventsPerStatus = 200;

    /// <summary>Liste hedefi için tek tip faz — GetStatus ile ResolvedListingPageUrl okumak için.</summary>
    private const string ListingTargetPhase = "listing";

    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IRepository<EcScrapeRunEvent, Guid> _eventRepository;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly CimriSeleniumPageFetcher _seleniumPageFetcher;
    private readonly CimriProductIngestionService _ingestionService;
    private readonly IListingSyncNotifier _listingSyncNotifier;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IScrapeRunEventLogger _eventLogger;
    private readonly ILogger<CimriListingSyncAppService> _logger;

    public CimriListingSyncAppService(
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IRepository<EcScrapeRunEvent, Guid> eventRepository,
        IOptionsMonitor<CimriClientOptions> options,
        CimriSeleniumPageFetcher seleniumPageFetcher,
        CimriProductIngestionService ingestionService,
        IListingSyncNotifier listingSyncNotifier,
        IBackgroundJobManager backgroundJobManager,
        IScrapeRunEventLogger eventLogger,
        ILogger<CimriListingSyncAppService> logger)
    {
        _scrapeRunRepository = scrapeRunRepository;
        _eventRepository = eventRepository;
        _options = options;
        _seleniumPageFetcher = seleniumPageFetcher;
        _ingestionService = ingestionService;
        _listingSyncNotifier = listingSyncNotifier;
        _backgroundJobManager = backgroundJobManager;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    public virtual async Task<CimriListingSyncResultDto> SyncAsync(CimriListingSyncInput? input)
    {
        var effectiveInput = input ?? new CimriListingSyncInput();
        var options = _options.CurrentValue;

        var maxPages = NormalizePositive(effectiveInput.MaxPages, options.DefaultMaxPages, 1, 200);
        var maxProducts = NormalizePositive(effectiveInput.MaxProducts, options.DefaultMaxProducts, 1, 5000);
        var includeOffers = effectiveInput.IncludeProductDetails ?? effectiveInput.IncludeOffers;
        var expandAllOffers = effectiveInput.ExpandAllOffers;

        var listingUrl = CimriListingUrlResolver.Resolve(options, effectiveInput.ListingPageUrl);
        var retailPolicy = CimriRetailOfferPolicyResolver.MergeFromSyncInput(options, effectiveInput);
        var loadMoreClicks = Math.Max(0, maxPages - 1);

        var scrapeRun = new EcScrapeRun(
            GuidGenerator.Create(),
            MarketplaceKind.Cimri,
            EcScrapeRunStatus.Running,
            Clock.Now,
            triggerSource: nameof(CimriListingSyncAppService));
        await _scrapeRunRepository.InsertAsync(scrapeRun, autoSave: true);

        var listingFromForm = !string.IsNullOrWhiteSpace(effectiveInput.ListingPageUrl?.Trim());
        var listingSourceLabel = listingFromForm
            ? "Senkron formu (tarayıcıdaki liste URL alanı)"
            : "Sunucu yapılandırması (Cimri:ListingPageUrl — form boş bırakıldı)";
        var listingSourceCode = listingFromForm ? "form" : "server";

        await _eventLogger.LogAsync(
            scrapeRun.Id, EcScrapeRunEventLevel.Info, ListingTargetPhase,
            $"[listingSource:{listingSourceCode}] {listingSourceLabel}. Aşağıdaki satır linkinde ve Worker işinde aynı tam HTTPS adresi kullanılır.",
            url: listingUrl);

        await _eventLogger.LogAsync(
            scrapeRun.Id, EcScrapeRunEventLevel.Info, "system",
            $"Senkron başlatıldı (sayfa: {maxPages}, max ürün: {maxProducts}).");

        if (options.UseQueue)
        {
            await _backgroundJobManager.EnqueueAsync(new CimriListingDiscoveryJobArgs
            {
                ScrapeRunId = scrapeRun.Id,
                MaxPages = maxPages,
                MaxProducts = maxProducts,
                IncludeOffers = includeOffers,
                ExpandAllOffers = expandAllOffers,
                ForceRefresh = effectiveInput.ForceRefresh,
                ListingPageUrl = listingUrl,
                RetailOfferPolicy = CimriRetailOfferPolicyResolver.ToJobPolicy(retailPolicy),
            });

            await _eventLogger.LogAsync(
                scrapeRun.Id, EcScrapeRunEventLevel.Info, "system",
                "Discovery işi RabbitMQ kuyruğuna alındı, worker pickup bekleniyor…");

            _logger.LogInformation(
                "Cimri listing discovery queue'ya atıldı (scrapeRunId={RunId})",
                scrapeRun.Id);

            return new CimriListingSyncResultDto
            {
                ScrapeRunId = scrapeRun.Id,
                PagesFetched = 0,
                ProductsAffected = 0,
                OffersAffected = 0,
                MerchantsAffected = 0,
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
            _logger.LogInformation(
                "Cimri sync başlıyor — {ListingUrl}, maxPages={MaxPages}, maxProducts={MaxProducts}, includeOffers={IncludeOffers}",
                listingUrl, maxPages, maxProducts, includeOffers);

            var html = await _seleniumPageFetcher.TryGetListingHtmlAsync(listingUrl, loadMoreClicks, options);
            if (string.IsNullOrWhiteSpace(html))
            {
                throw new BusinessException("BeeBAK:CimriListingHtmlEmpty")
                    .WithData("ListingUrl", listingUrl);
            }

            var cards = CimriListingCardSorter
                .SortByDiscountDescending(CimriListingHtmlParser.Parse(html, options.BaseUrl));
            pagesFetched = loadMoreClicks + 1;

            _logger.LogInformation(
                "Cimri listeleme: {Count} ayrı ürün kartı parse edildi (loadMoreClicks={Clicks})",
                cards.Count, loadMoreClicks);

            var trimmed = cards.Take(maxProducts).ToList();

            foreach (var card in trimmed)
            {
                try
                {
                    var ingestion = await _ingestionService.UpsertAsync(
                        card,
                        includeOffers,
                        expandAllOffers,
                        retailPolicy);

                    productsAffected++;
                    offersAffected += ingestion.OffersAdded;
                    foreach (var mid in ingestion.TouchedMerchantIds)
                    {
                        merchantsTouched.Add(mid);
                    }

                    if (options.DelayBetweenProductsMs > 0)
                    {
                        await Task.Delay(options.DelayBetweenProductsMs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Cimri ürün senkronu başarısız: {ContentId} {ProductUrl}",
                        card.ContentId, card.ProductUrl);
                }
            }

            scrapeRun.Complete(Clock.Now);
            await _scrapeRunRepository.UpdateAsync(scrapeRun, autoSave: true);

            await _listingSyncNotifier.NotifyListingSyncCompletedAsync(
                new ListingSyncNotificationContext(
                    MarketplaceKind.Cimri,
                    scrapeRun.Id,
                    productsAffected,
                    pagesFetched,
                    SearchQuery: listingUrl));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cimri sync hata aldı");
            scrapeRun.Fail(Clock.Now, ex.Message);
            await _scrapeRunRepository.UpdateAsync(scrapeRun, autoSave: true);
            throw;
        }

        return new CimriListingSyncResultDto
        {
            ScrapeRunId = scrapeRun.Id,
            PagesFetched = pagesFetched,
            ProductsAffected = productsAffected,
            OffersAffected = offersAffected,
            MerchantsAffected = merchantsTouched.Count,
            ResolvedListingPageUrl = listingUrl,
        };
    }

    public virtual async Task<CimriListingSyncStatusDto> GetStatusAsync(Guid scrapeRunId, DateTime? sinceUtc = null)
    {
        var run = await _scrapeRunRepository.FindAsync(scrapeRunId)
                  ?? throw new EntityNotFoundException(typeof(EcScrapeRun), scrapeRunId);

        var events = await LoadEventsAsync(scrapeRunId, sinceUtc);
        var listingMeta = await GetListingTargetMetaAsync(scrapeRunId);
        return MapToStatus(run, events, listingMeta.url, listingMeta.source);
    }

    public virtual async Task<CimriListingSyncStatusDto?> GetLatestAsync()
    {
        var queryable = await _scrapeRunRepository.GetQueryableAsync();
        var run = queryable
            .Where(r => r.Marketplace == MarketplaceKind.Cimri)
            .OrderByDescending(r => r.StartedUtc)
            .FirstOrDefault();
        if (run == null) return null;
        var events = await LoadEventsAsync(run.Id, null);
        var listingMeta = await GetListingTargetMetaAsync(run.Id);
        return MapToStatus(run, events, listingMeta.url, listingMeta.source);
    }

    [Authorize(BeeBAKPermissions.Cimri.Sync)]
    public virtual async Task<CimriListingSyncStatusDto> CancelAsync(Guid scrapeRunId)
    {
        var run = await _scrapeRunRepository.FindAsync(scrapeRunId)
                  ?? throw new EntityNotFoundException(typeof(EcScrapeRun), scrapeRunId);

        if (run.Status == EcScrapeRunStatus.Completed
            || run.Status == EcScrapeRunStatus.Failed
            || run.Status == EcScrapeRunStatus.Cancelled)
        {
            var events = await LoadEventsAsync(scrapeRunId, null);
            var listingMeta = await GetListingTargetMetaAsync(scrapeRunId);
            return MapToStatus(run, events, listingMeta.url, listingMeta.source);
        }

        run.RequestCancel();
        // Cancel istendiğinde run'ı doğrudan Cancelled'a geçir; kuyrukta kalan worker'lar
        // iş başında IsCancelled kontrolüyle erken çıkar, devam eden job'lar bittiğinde
        // TryFinalizeRunAsync zaten Status==Running kontrolü olduğu için bu state'i bozmaz.
        run.MarkCancelled(Clock.Now);
        await _scrapeRunRepository.UpdateAsync(run, autoSave: true);

        await _eventLogger.LogAsync(
            scrapeRunId, EcScrapeRunEventLevel.Warning, "system",
            "Kullanıcı iptal etti — devam eden işler nazikçe sonlandırılıyor.");

        _logger.LogInformation("Cimri scrape run iptal edildi: {RunId}", scrapeRunId);

        var afterEvents = await LoadEventsAsync(scrapeRunId, null);
        var listingMetaFinal = await GetListingTargetMetaAsync(scrapeRunId);
        return MapToStatus(run, afterEvents, listingMetaFinal.url, listingMetaFinal.source);
    }

    private async Task<(string? url, string? source)> GetListingTargetMetaAsync(Guid scrapeRunId)
    {
        var rows = await _eventRepository.GetListAsync(e =>
            e.ScrapeRunId == scrapeRunId && e.Phase == ListingTargetPhase);

        var row = rows.OrderBy(e => e.TimestampUtc).FirstOrDefault();
        if (row == null)
        {
            return (null, null);
        }

        string? source = null;
        if (row.Message.StartsWith("[listingSource:form]", StringComparison.OrdinalIgnoreCase))
        {
            source = "form";
        }
        else if (row.Message.StartsWith("[listingSource:server]", StringComparison.OrdinalIgnoreCase))
        {
            source = "server";
        }

        return (row.Url, source);
    }

    private async Task<List<CimriListingSyncEventDto>> LoadEventsAsync(Guid scrapeRunId, DateTime? sinceUtc)
    {
        var queryable = await _eventRepository.GetQueryableAsync();
        IQueryable<EcScrapeRunEvent> q = queryable.Where(x => x.ScrapeRunId == scrapeRunId);
        if (sinceUtc.HasValue)
        {
            q = q.Where(x => x.TimestampUtc > sinceUtc.Value);
        }

        // Son N event'i getirip sonra eski → yeni sıraya çeviriyoruz (UI append eder).
        var rows = q.OrderByDescending(x => x.TimestampUtc)
                    .Take(MaxEventsPerStatus)
                    .ToList()
                    .OrderBy(x => x.TimestampUtc)
                    .ToList();

        return rows.Select(e => new CimriListingSyncEventDto
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
        }).ToList();
    }

    private CimriListingSyncStatusDto MapToStatus(
        EcScrapeRun run,
        List<CimriListingSyncEventDto> events,
        string? resolvedListingPageUrl = null,
        string? listingPageSource = null)
    {
        var now = Clock.Now;
        var endTime = run.CompletedUtc ?? now;
        var elapsedSeconds = Math.Max(0, (endTime - run.StartedUtc).TotalSeconds);

        var progress = run.TotalItems > 0
            ? Math.Clamp((run.ProcessedItems + run.FailedItems) / (double)run.TotalItems, 0, 1)
            : 0;

        double? estimatedRemaining = null;
        var doneCount = run.ProcessedItems + run.FailedItems;
        if (run.Status == EcScrapeRunStatus.Running && run.TotalItems > 0 && doneCount > 0)
        {
            var perItem = elapsedSeconds / doneCount;
            var remainingItems = Math.Max(0, run.TotalItems - doneCount);
            estimatedRemaining = perItem * remainingItems;
        }

        var isActive = run.Status == EcScrapeRunStatus.Running
                       || run.Status == EcScrapeRunStatus.Pending;

        return new CimriListingSyncStatusDto
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
        if (input is null || input.Value < min)
        {
            return Math.Clamp(defaultValue, min, max);
        }

        return Math.Clamp(input.Value, min, max);
    }
}
