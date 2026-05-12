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
            _logger.LogWarning("Geçersiz Akakce job args (productCode/url boş)");
            return;
        }

        if (await IsCancelledAsync(args.ScrapeRunId))
        {
            _logger.LogInformation("Akakce detail iptal edildi (run cancelled): {ProductCode}", args.ProductCode);
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Warning, "detail",
                "İptal nedeniyle atlandı.", args.Title, args.ProductUrl);
            return;
        }

        if (!args.ForceRefresh && await _dedupCache.IsRecentlyVisitedAsync(args.ProductCode))
        {
            _logger.LogDebug("Akakce product detail skipped (recently visited): {ProductCode}", args.ProductCode);
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail",
                "Yakın zamanda işlenmiş, atlanıyor.", args.Title, args.ProductUrl);
            await IncrementProcessedAsync(args.ScrapeRunId);
            await TryFinalizeRunAsync(args.ScrapeRunId);
            return;
        }

        var lockKey = $"akakce:lock:{args.ProductCode}";
        // TimeSpan.Zero = sadece bir kez dene — aynı productCode zaten işleniyorsa atla (duplicate key önleme)
        var handle = _lockProvider != null
            ? await _lockProvider.TryAcquireLockAsync(lockKey, TimeSpan.Zero)
            : null;

        if (_lockProvider != null && handle == null)
        {
            _logger.LogInformation("Akakce product detail lock alınamadı, başkası işliyor: {ProductCode}", args.ProductCode);
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail",
                "Başka bir worker işliyor, atlanıyor.", args.Title, args.ProductUrl);
            await IncrementProcessedAsync(args.ScrapeRunId);
            await TryFinalizeRunAsync(args.ScrapeRunId);
            return;
        }

        await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail",
            "Detay sayfası açılıyor…", args.Title, args.ProductUrl);

        var success = false;
        try
        {
            var result = await ProcessAsync(args);

            if (result.MarkVisitedInDedupCache)
            {
                await _dedupCache.TryAcquireAsync(
                    args.ProductCode,
                    TimeSpan.FromSeconds(Math.Max(60, _options.CurrentValue.DedupTtlSeconds)));
            }

            if (result.MarkVisitedInDedupCache)
            {
                await _eventLogger.LogAsync(
                    args.ScrapeRunId, EcScrapeRunEventLevel.Success, "detail",
                    $"Kaydedildi — {result.OffersAdded} teklif, {result.TouchedMerchantIds.Count} mağaza.",
                    args.Title, args.ProductUrl);
            }
            else
            {
                await _eventLogger.LogAsync(
                    args.ScrapeRunId, EcScrapeRunEventLevel.Warning, "detail",
                    "Geçerli teklif bulunamadı — ürün kaydedilmedi.",
                    args.Title, args.ProductUrl);
            }

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Akakce product detail başarısız: {ProductCode}", args.ProductCode);
            await _eventLogger.LogAsync(args.ScrapeRunId, EcScrapeRunEventLevel.Error, "detail",
                $"Başarısız: {ex.Message}", args.Title, args.ProductUrl);
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
        var card = new AkakceListingCard
        {
            ProductCode         = args.ProductCode,
            ProductUrl          = args.ProductUrl,
            Title               = args.Title ?? string.Empty,
            BrandName           = args.BrandName,
            ImageUrl            = args.ImageUrl,
            BestPriceAmount     = args.BestPriceAmount,
            PreviousPriceAmount = args.PreviousPriceAmount,
            OfferCount          = args.OfferCount,
            DiscountPercent     = args.DiscountPercent,
        };

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var uow = _unitOfWorkManager.Begin(requiresNew: true);
                var result = await _ingestionService.UpsertAsync(card, args.IncludeOffers);
                await uow.CompleteAsync();

                _logger.LogInformation(
                    "Akakce product detail done: {ProductCode} (productId={ProductId}, offers={Offers}, merchants={Merchants})",
                    args.ProductCode, result.ProductId, result.OffersAdded, result.TouchedMerchantIds.Count);

                return result;
            }
            catch (Exception ex) when (attempt == 1 && IsDuplicateKeyException(ex))
            {
                _logger.LogDebug("Akakce duplicate key, retrying as update: {ProductCode}", args.ProductCode);
            }
        }

        throw new InvalidOperationException("Akakce ProcessAsync retry exhausted — should not reach here");
    }

    private static bool IsDuplicateKeyException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var msg = current.Message;
            if (msg.Contains("23505", StringComparison.Ordinal) ||
                msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("IX_AppAkakceProducts", StringComparison.Ordinal))
                return true;
        }
        return false;
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
