using System;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri.Logging;
using Medallion.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace BeeBAK.Marketplaces.Akakce.Jobs;

public class AkakceProductDetailJob : AsyncBackgroundJob<AkakceProductDetailJobArgs>
{
    private readonly AkakceProductIngestionService _ingestionService;
    private readonly IAkakceDedupCache _dedupCache;
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly IOptionsMonitor<AkakceClientOptions> _options;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IClock _clock;
    private readonly IScrapeRunEventLogger _eventLogger;
    private readonly ILogger<AkakceProductDetailJob> _logger;

    public AkakceProductDetailJob(
        AkakceProductIngestionService ingestionService,
        IAkakceDedupCache dedupCache,
        IOptionsMonitor<AkakceClientOptions> options,
        IUnitOfWorkManager unitOfWorkManager,
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IClock clock,
        IScrapeRunEventLogger eventLogger,
        ILogger<AkakceProductDetailJob> logger,
        IDistributedLockProvider? lockProvider = null)
    {
        _ingestionService = ingestionService;
        _dedupCache = dedupCache;
        _options = options;
        _unitOfWorkManager = unitOfWorkManager;
        _scrapeRunRepository = scrapeRunRepository;
        _clock = clock;
        _eventLogger = eventLogger;
        _logger = logger;
        _lockProvider = lockProvider;
    }

    public override async Task ExecuteAsync(AkakceProductDetailJobArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.ProductCode) || string.IsNullOrWhiteSpace(args.ProductUrl))
        {
            _logger.LogWarning("Invalid Akakce job args");
            return;
        }

        if (await IsCancelledAsync(args.ScrapeRunId))
        {
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Warning, "detail", "Cancelled before start.", args.Title, args.ProductUrl);
            return;
        }

        if (!args.ForceRefresh && await _dedupCache.IsRecentlyVisitedAsync(args.ProductCode))
        {
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail", "Recently visited, skipped.", args.Title, args.ProductUrl);
            await IncrementProcessedAsync(args.ScrapeRunId);
            await TryFinalizeRunAsync(args.ScrapeRunId);
            return;
        }

        var lockKey = $"akakce:lock:{args.ProductCode}";
        var handle = _lockProvider != null
            ? await _lockProvider.TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(30))
            : null;

        if (_lockProvider != null && handle == null)
        {
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail", "Another worker is processing this product.", args.Title, args.ProductUrl);
            await IncrementProcessedAsync(args.ScrapeRunId);
            await TryFinalizeRunAsync(args.ScrapeRunId);
            return;
        }

        var success = false;
        try
        {
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail", "Detail page opening...", args.Title, args.ProductUrl);
            var result = await ProcessAsync(args);

            if (result.MarkVisitedInDedupCache)
            {
                await _dedupCache.TryAcquireAsync(
                    args.ProductCode,
                    TimeSpan.FromSeconds(Math.Max(60, _options.CurrentValue.DedupTtlSeconds)));
            }

            await _eventLogger.LogAsync(
                args.ScrapeRunId, EcScrapeRunEventLevel.Success, "detail",
                $"Saved - {result.OffersAdded} offers, {result.TouchedMerchantIds.Count} merchants.",
                args.Title, args.ProductUrl);
            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Akakce product detail failed: {ProductCode}", args.ProductCode);
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Error, "detail", $"Failed: {ex.Message}", args.Title, args.ProductUrl);
            await IncrementFailedAsync(args.ScrapeRunId);
            throw;
        }
        finally
        {
            if (success)
            {
                await IncrementProcessedAsync(args.ScrapeRunId);
            }

            await TryFinalizeRunAsync(args.ScrapeRunId);

            if (handle is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (handle is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private async Task<AkakceIngestionResult> ProcessAsync(AkakceProductDetailJobArgs args)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var card = new AkakceListingCard
        {
            ProductCode = args.ProductCode,
            ProductUrl = args.ProductUrl,
            Title = args.Title ?? string.Empty,
            BrandName = args.BrandName,
            ImageUrl = args.ImageUrl,
            BestPriceAmount = args.BestPriceAmount,
            PreviousPriceAmount = args.PreviousPriceAmount,
            OfferCount = args.OfferCount,
            DiscountPercent = args.DiscountPercent,
        };

        var result = await _ingestionService.UpsertAsync(card, args.IncludeOffers);
        await uow.CompleteAsync();
        return result;
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

    private Task IncrementFailedAsync(Guid scrapeRunId)
        => MutateRunAsync(scrapeRunId, run => run.IncrementFailed(), "failed inc");

    private async Task MutateRunAsync(Guid scrapeRunId, Action<EcScrapeRun> mutate, string opName)
    {
        if (scrapeRunId == Guid.Empty) return;
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
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
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsConcurrencyException(ex))
            {
                await Task.Delay(20 * attempt);
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Akakce scrape run {Op} failed: {RunId}", opName, scrapeRunId);
                return;
            }
        }
    }

    private static bool IsConcurrencyException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException!)
        {
            var name = current.GetType().Name;
            if (name == "DbUpdateConcurrencyException" || name == "AbpDbConcurrencyException")
            {
                return true;
            }
        }

        return false;
    }

    private async Task TryFinalizeRunAsync(Guid scrapeRunId)
    {
        if (scrapeRunId == Guid.Empty) return;
        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true);
            var run = await _scrapeRunRepository.FindAsync(scrapeRunId);
            if (run != null
                && run.Status == EcScrapeRunStatus.Running
                && run.TotalItems > 0
                && (run.ProcessedItems + run.FailedItems) >= run.TotalItems)
            {
                if (run.CancelRequested) run.MarkCancelled(_clock.Now);
                else run.Complete(_clock.Now);
                await _scrapeRunRepository.UpdateAsync(run, autoSave: true);
            }
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Akakce scrape run finalize failed: {RunId}", scrapeRunId);
        }
    }
}
