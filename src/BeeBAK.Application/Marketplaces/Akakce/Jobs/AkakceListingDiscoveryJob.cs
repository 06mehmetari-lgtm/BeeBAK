using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace BeeBAK.Marketplaces.Akakce.Jobs;

public class AkakceListingDiscoveryJob : AsyncBackgroundJob<AkakceListingDiscoveryJobArgs>
{
    private readonly AkakceSeleniumPageFetcher _seleniumPageFetcher;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IAkakceDedupCache _dedupCache;
    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IOptionsMonitor<AkakceClientOptions> _options;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IScrapeRunEventLogger _eventLogger;
    private readonly ILogger<AkakceListingDiscoveryJob> _logger;

    public AkakceListingDiscoveryJob(
        AkakceSeleniumPageFetcher seleniumPageFetcher,
        IBackgroundJobManager backgroundJobManager,
        IAkakceDedupCache dedupCache,
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IOptionsMonitor<AkakceClientOptions> options,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        IScrapeRunEventLogger eventLogger,
        ILogger<AkakceListingDiscoveryJob> logger)
    {
        _seleniumPageFetcher = seleniumPageFetcher;
        _backgroundJobManager = backgroundJobManager;
        _dedupCache = dedupCache;
        _scrapeRunRepository = scrapeRunRepository;
        _options = options;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    public override async Task ExecuteAsync(AkakceListingDiscoveryJobArgs args)
    {
        var options = _options.CurrentValue;
        var maxPages = Math.Clamp(args.MaxPages <= 0 ? options.DefaultMaxPages : args.MaxPages, 1, 200);
        var maxProducts = Math.Clamp(args.MaxProducts <= 0 ? options.DefaultMaxProducts : args.MaxProducts, 1, 5000);
        var detailBatchSize = Math.Clamp(options.ProductDetailEnqueueBatchSize, 1, 500);
        var listingUrl = AkakceListingUrlResolver.Resolve(options, args.ListingPageUrl);

        await _eventLogger.LogAsync(
            args.ScrapeRunId, EcScrapeRunEventLevel.Info, "discovery",
            $"Worker: Akakce listing starts. pages={maxPages}, maxProducts={maxProducts}.",
            url: listingUrl);

        if (await IsCancelledAsync(args.ScrapeRunId))
        {
            await MarkRunCancelledAsync(args.ScrapeRunId);
            return;
        }

        if (await IsCancelledAsync(args.ScrapeRunId))
        {
            await MarkRunCancelledAsync(args.ScrapeRunId);
            return;
        }

        // ── Tüm sayfaları paralel olarak Selenium'dan çek ──────────────
        var pageUrls = Enumerable.Range(1, maxPages)
            .Select(p => (page: p, url: AkakceListingUrlResolver.BuildPageUrl(listingUrl, p)))
            .ToList();

        await _eventLogger.LogAsync(
            args.ScrapeRunId, EcScrapeRunEventLevel.Info, "discovery",
            $"{maxPages} sayfa paralel olarak çekiliyor (Selenium Grid).",
            url: listingUrl, index: 0, total: maxPages);

        // Aynı anda en fazla 3 Selenium oturumu (CX33: 2 node × 2 session = 4 kapasite; 3 paralel bellek güvenli)
        var sem = new System.Threading.SemaphoreSlim(3, 3);

        var fetchTasks = pageUrls.Select(async item =>
        {
            await sem.WaitAsync();
            try
            {
                var html = await _seleniumPageFetcher.TryGetListingHtmlAsync(item.url, options);
                return (item.page, item.url, html, error: (string?)null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Akakce page fetch failed (page={Page}, url={Url}): {Msg}",
                    item.page, item.url, ex.Message);
                return (item.page, item.url, html: (string?)null, error: ex.Message);
            }
            finally { sem.Release(); }
        }).ToList();

        var pageResults = await System.Threading.Tasks.Task.WhenAll(fetchTasks);

        // ── Gelen sayfaları işle ────────────────────────────────────────
        var cardsByCode = new Dictionary<string, AkakceListingCard>(StringComparer.Ordinal);
        var successfulPages = 0;

        foreach (var (page, pageUrl, html, error) in pageResults.OrderBy(r => r.page))
        {
            if (error != null)
            {
                await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Warning,
                    "discovery", $"Sayfa {page} alınamadı: {error}", url: pageUrl, index: page, total: maxPages);
                continue;
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Warning,
                    "discovery", $"Sayfa {page} boş HTML döndürdü.", url: pageUrl, index: page, total: maxPages);
                continue;
            }

            successfulPages++;
            var candidateCount = AkakceListingHtmlParser.CountCandidateCards(html);
            var pageCards      = AkakceListingHtmlParser.Parse(html, options.BaseUrl);

            await _eventLogger.LogAsync(
                args.ScrapeRunId, EcScrapeRunEventLevel.Info, "discovery",
                $"Sayfa {page}: {pageCards.Count} ürün kartı parse edildi.",
                url: pageUrl, index: page, total: maxPages);

            if (candidateCount > pageCards.Count)
            {
                await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Warning,
                    "discovery",
                    $"Sayfa {page}: {candidateCount - pageCards.Count} kart detay linki olmadığı için atlandı.",
                    url: pageUrl, index: page, total: maxPages);
            }

            foreach (var card in pageCards)
            {
                if (cardsByCode.Count >= maxProducts) break;
                cardsByCode.TryAdd(card.ProductCode, card);
            }
        }

        if (successfulPages == 0)
        {
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Error,
                "discovery", "Hiçbir sayfa alınamadı — tüm Selenium istekleri başarısız.");
            await MarkRunFailedAsync(args.ScrapeRunId, "All page fetches failed");
            throw new BusinessException("BeeBAK:AkakceListingHtmlEmpty").WithData("ListingUrl", listingUrl);
        }

        var cards = AkakceListingCardSorter.SortByDiscountDescending(cardsByCode.Values).Take(maxProducts).ToList();
        await SetTotalItemsAsync(args.ScrapeRunId, cards.Count);

        if (cards.Count == 0)
        {
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Warning, "discovery", "No Akakce product cards found.");
            await CompleteRunAsync(args.ScrapeRunId);
            return;
        }

        var enqueued = 0;
        var skipped = 0;
        var index = 0;
        var batch = detailBatchSize > 1 ? new List<AkakceProductDetailJobArgs>() : null;
        foreach (var card in cards)
        {
            index++;
            if (await IsCancelledAsync(args.ScrapeRunId))
            {
                batch?.Clear();
                await MarkRunCancelledAsync(args.ScrapeRunId);
                return;
            }

            if (!args.ForceRefresh && await _dedupCache.IsRecentlyVisitedAsync(card.ProductCode))
            {
                skipped++;
                await IncrementProcessedAsync(args.ScrapeRunId);
                await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Info, "discovery", "Recently visited, skipped.", card.Title, card.ProductUrl, index, cards.Count);
                continue;
            }

            var detailArgs = new AkakceProductDetailJobArgs
            {
                ScrapeRunId = args.ScrapeRunId,
                ProductCode = card.ProductCode,
                ProductUrl = card.ProductUrl,
                Title = card.Title,
                BrandName = card.BrandName,
                ImageUrl = card.ImageUrl,
                BestPriceAmount = card.BestPriceAmount,
                PreviousPriceAmount = card.PreviousPriceAmount,
                OfferCount = card.OfferCount,
                DiscountPercent = card.DiscountPercent,
                IncludeOffers = args.IncludeOffers,
                ForceRefresh = args.ForceRefresh,
            };

            if (detailBatchSize <= 1)
            {
                await _backgroundJobManager.EnqueueAsync(detailArgs);
            }
            else
            {
                batch!.Add(detailArgs);
                if (batch.Count >= detailBatchSize)
                {
                    await EnqueueBatchAsync(args, batch);
                }
            }

            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Info, "discovery", "Detail job queued.", card.Title, card.ProductUrl, index, cards.Count);
            enqueued++;
        }

        if (batch is { Count: > 0 })
        {
            await EnqueueBatchAsync(args, batch);
        }

        await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Success, "discovery", $"Discovery done: {enqueued} queued, {skipped} skipped.");
        if (enqueued == 0)
        {
            await CompleteRunAsync(args.ScrapeRunId);
        }
    }

    private async Task EnqueueBatchAsync(AkakceListingDiscoveryJobArgs args, List<AkakceProductDetailJobArgs> batch)
    {
        await _backgroundJobManager.EnqueueAsync(new AkakceProductDetailBatchJobArgs
        {
            ScrapeRunId = args.ScrapeRunId,
            Items = batch.ToList(),
        });
        batch.Clear();
    }

    private async Task<bool> IsCancelledAsync(Guid scrapeRunId)
    {
        if (scrapeRunId == Guid.Empty) return false;
        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true);
            var run = await _scrapeRunRepository.FindAsync(scrapeRunId);
            await uow.CompleteAsync();
            return run != null && (run.CancelRequested || run.Status == EcScrapeRunStatus.Cancelled);
        }
        catch { return false; }
    }

    private Task IncrementProcessedAsync(Guid scrapeRunId)
        => MutateRunAsync(scrapeRunId, run => run.IncrementProcessed(), "processed inc");

    private Task SetTotalItemsAsync(Guid scrapeRunId, int total)
        => MutateRunAsync(scrapeRunId, run => run.SetTotalItems(total), "total set");

    private Task CompleteRunAsync(Guid scrapeRunId)
        => MutateRunAsync(scrapeRunId, run =>
        {
            if (run.Status == EcScrapeRunStatus.Running) run.Complete(_clock.Now);
        }, "complete");

    private Task MarkRunCancelledAsync(Guid scrapeRunId)
        => MutateRunAsync(scrapeRunId, run => run.MarkCancelled(_clock.Now), "cancel");

    private Task MarkRunFailedAsync(Guid scrapeRunId, string message)
        => MutateRunAsync(scrapeRunId, run => run.Fail(_clock.Now, message), "fail");

    private async Task MutateRunAsync(Guid scrapeRunId, Action<EcScrapeRun> mutate, string opName)
    {
        if (scrapeRunId == Guid.Empty) return;
        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true);
            var run = await _scrapeRunRepository.FindAsync(scrapeRunId);
            if (run != null)
            {
                mutate(run);
                await _scrapeRunRepository.UpdateAsync(run, autoSave: true);
            }
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Akakce scrape run {Op} failed: {RunId}", opName, scrapeRunId);
        }
    }
}
