using System;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Marketplaces.Cimri.Logging;
using Medallion.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

/// <summary>
/// Tek bir Cimri ürün PDP'sini çeken, mağaza redirect URL'lerini takip eden ve DB'ye yazan worker job'u.
/// Distributed lock + dedup cache ile aynı contentId'yi paralel iki worker'da işlenmesini engeller.
/// </summary>
public class CimriProductDetailJob : AsyncBackgroundJob<CimriProductDetailJobArgs>
{
    private readonly CimriProductIngestionService _ingestionService;
    private readonly ICimriDedupCache _dedupCache;
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IClock _clock;
    private readonly IScrapeRunEventLogger _eventLogger;
    private readonly ILogger<CimriProductDetailJob> _ownLogger;

    public CimriProductDetailJob(
        CimriProductIngestionService ingestionService,
        ICimriDedupCache dedupCache,
        IOptionsMonitor<CimriClientOptions> options,
        IUnitOfWorkManager unitOfWorkManager,
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IClock clock,
        IScrapeRunEventLogger eventLogger,
        ILogger<CimriProductDetailJob> logger,
        IDistributedLockProvider? lockProvider = null)
    {
        _ingestionService = ingestionService;
        _dedupCache = dedupCache;
        _options = options;
        _unitOfWorkManager = unitOfWorkManager;
        _scrapeRunRepository = scrapeRunRepository;
        _clock = clock;
        _eventLogger = eventLogger;
        _ownLogger = logger;
        _lockProvider = lockProvider;
    }

    public override async Task ExecuteAsync(CimriProductDetailJobArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.ContentId) || string.IsNullOrWhiteSpace(args.ProductUrl))
        {
            _ownLogger.LogWarning("Geçersiz job args (contentId/url boş)");
            return;
        }

        if (await IsCancelledAsync(args.ScrapeRunId))
        {
            _ownLogger.LogInformation("Cimri detail iptal edildi (run cancelled): {ContentId}", args.ContentId);
            await _eventLogger.LogAsync(
                args.ScrapeRunId, EcScrapeRunEventLevel.Warning, "detail",
                "İptal nedeniyle atlandı.", title: args.Title, url: args.ProductUrl);
            return;
        }

        if (!args.ForceRefresh)
        {
            if (await _dedupCache.IsRecentlyVisitedAsync(args.ContentId))
            {
                _ownLogger.LogDebug("Cimri product detail skipped (recently visited): {ContentId}", args.ContentId);
                await _eventLogger.LogAsync(
                    args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail",
                    "Yakın zamanda işlenmiş, atlanıyor.", title: args.Title, url: args.ProductUrl);
                await IncrementProcessedAsync(args.ScrapeRunId);
                await TryFinalizeRunAsync(args.ScrapeRunId);
                return;
            }
        }

        var lockKey = $"cimri:lock:{args.ContentId}";
        // TimeSpan.Zero = sadece bir kez dene, bekleme: aynı contentId zaten işleniyorsa atla
        var handle = _lockProvider != null
            ? await _lockProvider.TryAcquireLockAsync(lockKey, TimeSpan.Zero)
            : null;

        if (_lockProvider != null && handle == null)
        {
            _ownLogger.LogInformation("Cimri product detail lock alınamadı, başkası işliyor: {ContentId}", args.ContentId);
            await _eventLogger.LogAsync(
                args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail",
                "Başka bir worker işliyor, atlanıyor.", title: args.Title, url: args.ProductUrl);
            await IncrementProcessedAsync(args.ScrapeRunId);
            await TryFinalizeRunAsync(args.ScrapeRunId);
            return;
        }

        await _eventLogger.LogAsync(
            args.ScrapeRunId, EcScrapeRunEventLevel.Info, "detail",
            "Detay sayfası açılıyor…", title: args.Title, url: args.ProductUrl);

        var success = false;
        try
        {
            var result = await ProcessAsync(args);

            if (result.MarkVisitedInDedupCache)
            {
                await _dedupCache.TryAcquireAsync(
                    args.ContentId,
                    TimeSpan.FromSeconds(Math.Max(60, _options.CurrentValue.DedupTtlSeconds)));
            }

            if (result.MarkVisitedInDedupCache)
            {
                await _eventLogger.LogAsync(
                    args.ScrapeRunId, EcScrapeRunEventLevel.Success, "detail",
                    $"Kaydedildi — {result.OffersAdded} teklif, {result.TouchedMerchantIds.Count} mağaza.",
                    title: args.Title, url: args.ProductUrl);
            }
            else
            {
                await _eventLogger.LogAsync(
                    args.ScrapeRunId, EcScrapeRunEventLevel.Warning, "detail",
                    "İzin verilen mağazada geçerli teklif veya mağaza ürün kimliği yok — ürün kaydedilmedi.",
                    title: args.Title, url: args.ProductUrl);
            }

            success = true;
        }
        catch (Exception ex)
        {
            _ownLogger.LogWarning(ex, "Cimri product detail başarısız: {ContentId}", args.ContentId);
            await _eventLogger.LogAsync(
                args.ScrapeRunId, EcScrapeRunEventLevel.Error, "detail",
                $"Başarısız: {ex.Message}", title: args.Title, url: args.ProductUrl);
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

            if (handle is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
            else if (handle is IDisposable syncDisposable)
            {
                syncDisposable.Dispose();
            }
        }
    }

    private async Task<CimriIngestionResult> ProcessAsync(CimriProductDetailJobArgs args)
    {
        var card = new CimriListingCard
        {
            ContentId = args.ContentId,
            ProductUrl = args.ProductUrl,
            Title = args.Title ?? string.Empty,
            CategorySlug = args.CategorySlug,
            ImageUrl = args.ImageUrl,
            BestPriceAmount = args.BestPriceAmount,
            BestMerchantName = args.BestMerchantName,
            PreviousPriceAmount = args.PreviousPriceAmount,
            OfferCount = args.OfferCount,
            DiscountPercent = args.DiscountPercent,
        };

        var policy = CimriRetailOfferPolicyResolver.Resolve(_options.CurrentValue, args.RetailOfferPolicy);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var uow = _unitOfWorkManager.Begin(requiresNew: true);
                var result = await _ingestionService.UpsertAsync(card, args.IncludeOffers, args.ExpandAllOffers, policy);
                await uow.CompleteAsync();

                _ownLogger.LogInformation(
                    "Cimri product detail done: {ContentId} (productId={ProductId}, offers={Offers}, merchants={Merchants})",
                    args.ContentId, result.ProductId, result.OffersAdded, result.TouchedMerchantIds.Count);

                return result;
            }
            catch (Exception ex) when (attempt == 1 && IsDuplicateKeyException(ex))
            {
                // Worker restart sonrası başka bir job bu ürünü zaten ekledi.
                // UoW'u at, yeni UoW ile tekrar dene — bu sefer Find mevcut kaydı bulur.
                _ownLogger.LogDebug("Cimri duplicate key, retrying as update: {ContentId}", args.ContentId);
            }
        }

        throw new InvalidOperationException("Cimri ProcessAsync retry exhausted — should not reach here");
    }

    private static bool IsDuplicateKeyException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var msg = current.Message;
            if (msg.Contains("23505", StringComparison.Ordinal) ||
                msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("IX_AppCimriProducts", StringComparison.Ordinal))
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

    /// <summary>Concurrency conflict'lerinde basit exponential retry — paralel detail job'ların
    /// counter güncellemelerinde sayım kaybı olmaması için.</summary>
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
                _ownLogger.LogTrace(ex, "ScrapeRun {Op} başarısız: {RunId}", opName, scrapeRunId);
                return;
            }
        }
    }

    /// <summary>
    /// Run.TotalItems bilinir ve processed+failed >= total ise run'u Complete (veya Cancelled) yapar.
    /// Aynı anda birden çok detail job çalıştığı için son tamamlanan worker bu kontrolü düşürür.
    /// </summary>
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
                if (run.CancelRequested)
                {
                    run.MarkCancelled(_clock.Now);
                }
                else
                {
                    run.Complete(_clock.Now);
                }
                await _scrapeRunRepository.UpdateAsync(run, autoSave: true);
            }
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            _ownLogger.LogTrace(ex, "ScrapeRun finalize başarısız: {RunId}", scrapeRunId);
        }
    }
}
