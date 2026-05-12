using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Akakce.Jobs;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace BeeBAK.Marketplaces.Akakce;

/// <summary>
/// Robot 2 — Son N günde eklenen Akakce ürünlerini periyodik olarak yeniden tarar.
/// Fiyat düştüyse / indirim arttıysa / en ucuz satıcı değiştiyse Telegram pipeline'ı tetiklenir.
/// </summary>
public class AkakcePriceWatchWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string MutexKey = "akakce:pricewatch:mutex";

    public AkakcePriceWatchWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<AkakceClientOptions> options)
        : base(timer, serviceScopeFactory)
    {
        var pw = options.Value.PriceWatch;
        Timer.Period = (int)TimeSpan.FromMinutes(Math.Max(30, pw.IntervalMinutes)).TotalMilliseconds;
        Timer.RunOnStart = pw.RunOnStart;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var options = sp.GetRequiredService<IOptionsMonitor<AkakceClientOptions>>().CurrentValue;

        if (!options.PriceWatch.Enabled) return;

        var logger = sp.GetRequiredService<ILogger<AkakcePriceWatchWorker>>();

        var lockProvider = sp.GetService<IDistributedLockProvider>();
        IAsyncDisposable? mutexHandle = null;
        if (lockProvider != null)
        {
            mutexHandle = await lockProvider.TryAcquireLockAsync(MutexKey, TimeSpan.Zero);
            if (mutexHandle == null)
            {
                logger.LogInformation("AkakcePriceWatch: diğer replica mutex'i tuttu — bu tur atlanıyor.");
                return;
            }
        }

        try
        {
            await RunWatchAsync(sp, options, logger);
        }
        finally
        {
            if (mutexHandle != null) await mutexHandle.DisposeAsync();
        }
    }

    private static async Task RunWatchAsync(
        IServiceProvider sp,
        AkakceClientOptions options,
        ILogger<AkakcePriceWatchWorker> logger)
    {
        var pw            = options.PriceWatch;
        var productRepo   = sp.GetRequiredService<IRepository<AkakceProduct, Guid>>();
        var scrapeRunRepo = sp.GetRequiredService<IRepository<EcScrapeRun, Guid>>();
        var uowManager    = sp.GetRequiredService<IUnitOfWorkManager>();
        var clock         = sp.GetRequiredService<IClock>();
        var guidGenerator = sp.GetRequiredService<IGuidGenerator>();
        var jobManager    = sp.GetRequiredService<IBackgroundJobManager>();

        var ageCutoff    = DateTime.UtcNow - TimeSpan.FromDays(pw.ProductAgeDays);
        var resyncCutoff = DateTime.UtcNow - TimeSpan.FromMinutes(pw.ResyncAfterMinutes);
        var maxProducts  = Math.Max(1, pw.MaxProductsPerRun);

        List<AkakceProduct> candidates;
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            candidates = await productRepo.GetListAsync(p =>
                p.IsActive &&
                p.CreationTime >= ageCutoff &&
                (p.LastSyncedUtc == null || p.LastSyncedUtc < resyncCutoff));
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AkakcePriceWatch: ürün listesi alınamadı, tur atlanıyor.");
            return;
        }

        if (candidates.Count == 0)
        {
            logger.LogInformation("AkakcePriceWatch: yeniden taranacak ürün yok.");
            return;
        }

        var batch = candidates
            .OrderBy(p => p.LastSyncedUtc ?? DateTime.MinValue)
            .Take(maxProducts)
            .ToList();

        logger.LogInformation(
            "AkakcePriceWatch: {Count} ürün yeniden taranmak üzere kuyruğa alınıyor (toplam aday: {Total}).",
            batch.Count, candidates.Count);

        EcScrapeRun scrapeRun;
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            scrapeRun = new EcScrapeRun(
                guidGenerator.Create(),
                MarketplaceKind.Akakce,
                EcScrapeRunStatus.Running,
                clock.Now,
                triggerSource: nameof(AkakcePriceWatchWorker));
            await scrapeRunRepo.InsertAsync(scrapeRun, autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AkakcePriceWatch: tarama kaydı oluşturulamadı.");
            return;
        }

        try
        {
            var batchSize = Math.Max(1, options.ProductDetailEnqueueBatchSize);
            var items = batch.Select(p => new AkakceProductDetailJobArgs
            {
                ScrapeRunId         = scrapeRun.Id,
                ProductCode         = p.ProductCode,
                ProductUrl          = p.ProductUrl,
                Title               = p.Title,
                BrandName           = p.BrandName,
                BestPriceAmount     = p.BestPriceAmount,
                PreviousPriceAmount = p.PreviousPriceAmount,
                DiscountPercent     = p.DiscountPercent,
                IncludeOffers       = true,
                ForceRefresh        = true,   // dedup TTL'i atla, fiyatı zorla güncelle
            }).ToList();

            for (int i = 0; i < items.Count; i += batchSize)
            {
                var chunk = items.Skip(i).Take(batchSize).ToList();
                await jobManager.EnqueueAsync(new AkakceProductDetailBatchJobArgs
                {
                    ScrapeRunId = scrapeRun.Id,
                    Items       = chunk,
                });
            }

            logger.LogInformation(
                "AkakcePriceWatch: {Count} ürün {Batches} batch halinde kuyruğa alındı (runId={RunId}).",
                items.Count, (int)Math.Ceiling(items.Count / (double)batchSize), scrapeRun.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AkakcePriceWatch: kuyruğa atma başarısız (runId={RunId}).", scrapeRun.Id);
            try
            {
                using var uow = uowManager.Begin(requiresNew: true);
                var run = await scrapeRunRepo.FindAsync(scrapeRun.Id);
                if (run != null) { run.Fail(clock.Now, ex.Message); await scrapeRunRepo.UpdateAsync(run, autoSave: true); }
                await uow.CompleteAsync();
            }
            catch { /* best-effort */ }
        }
    }
}
