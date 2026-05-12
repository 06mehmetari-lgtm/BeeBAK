using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri.Jobs;
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

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Robot 2 — Son N günde eklenen ürünleri periyodik olarak yeniden tarar.
/// Fiyat düştüyse / indirim arttıysa / en ucuz satıcı değiştiyse mevcut Telegram
/// pipeline'ı kanalıyla otomatik paylaşım tetiklenir.
/// </summary>
public class CimriPriceWatchWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string MutexKey = "cimri:pricewatch:mutex";

    public CimriPriceWatchWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<CimriClientOptions> options)
        : base(timer, serviceScopeFactory)
    {
        var pw = options.Value.PriceWatch;
        Timer.Period = (int)TimeSpan.FromMinutes(Math.Max(30, pw.IntervalMinutes)).TotalMilliseconds;
        Timer.RunOnStart = pw.RunOnStart;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var options = sp.GetRequiredService<IOptionsMonitor<CimriClientOptions>>().CurrentValue;

        if (!options.PriceWatch.Enabled) return;

        var logger = sp.GetRequiredService<ILogger<CimriPriceWatchWorker>>();

        var lockProvider = sp.GetService<IDistributedLockProvider>();
        IAsyncDisposable? mutexHandle = null;
        if (lockProvider != null)
        {
            mutexHandle = await lockProvider.TryAcquireLockAsync(MutexKey, TimeSpan.Zero);
            if (mutexHandle == null)
            {
                logger.LogInformation("CimriPriceWatch: diğer replica mutex'i tuttu — bu tur atlanıyor.");
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
        CimriClientOptions options,
        ILogger<CimriPriceWatchWorker> logger)
    {
        var pw            = options.PriceWatch;
        var productRepo   = sp.GetRequiredService<IRepository<CimriProduct, Guid>>();
        var scrapeRunRepo = sp.GetRequiredService<IRepository<EcScrapeRun, Guid>>();
        var uowManager    = sp.GetRequiredService<IUnitOfWorkManager>();
        var clock         = sp.GetRequiredService<IClock>();
        var guidGenerator = sp.GetRequiredService<IGuidGenerator>();
        var jobManager    = sp.GetRequiredService<IBackgroundJobManager>();

        var ageCutoff    = DateTime.UtcNow - TimeSpan.FromDays(pw.ProductAgeDays);
        var resyncCutoff = DateTime.UtcNow - TimeSpan.FromMinutes(pw.ResyncAfterMinutes);
        var maxProducts  = Math.Max(1, pw.MaxProductsPerRun);

        List<CimriProduct> candidates;
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
            logger.LogWarning(ex, "CimriPriceWatch: ürün listesi alınamadı, tur atlanıyor.");
            return;
        }

        if (candidates.Count == 0)
        {
            logger.LogInformation("CimriPriceWatch: yeniden taranacak ürün yok.");
            return;
        }

        var batch = candidates
            .OrderBy(p => p.LastSyncedUtc ?? DateTime.MinValue)
            .Take(maxProducts)
            .ToList();

        logger.LogInformation(
            "CimriPriceWatch: {Count} ürün yeniden taranmak üzere kuyruğa alınıyor (toplam aday: {Total}).",
            batch.Count, candidates.Count);

        EcScrapeRun scrapeRun;
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            scrapeRun = new EcScrapeRun(
                guidGenerator.Create(),
                MarketplaceKind.Cimri,
                EcScrapeRunStatus.Running,
                clock.Now,
                triggerSource: nameof(CimriPriceWatchWorker));
            await scrapeRunRepo.InsertAsync(scrapeRun, autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CimriPriceWatch: tarama kaydı oluşturulamadı.");
            return;
        }

        try
        {
            var batchSize = Math.Max(1, options.ProductDetailEnqueueBatchSize);
            var items = batch.Select(p => new CimriProductDetailJobArgs
            {
                ScrapeRunId      = scrapeRun.Id,
                ContentId        = p.ContentId,
                ProductUrl       = p.ProductUrl,
                Title            = p.Title,
                BestPriceAmount  = p.BestPriceAmount,
                PreviousPriceAmount = p.PreviousPriceAmount,
                DiscountPercent  = p.DiscountPercent,
                IncludeOffers    = true,
                ExpandAllOffers  = true,
                ForceRefresh     = true,   // dedup TTL'i atla, fiyatı zorla güncelle
            }).ToList();

            for (int i = 0; i < items.Count; i += batchSize)
            {
                var chunk = items.Skip(i).Take(batchSize).ToList();
                await jobManager.EnqueueAsync(new CimriProductDetailBatchJobArgs
                {
                    ScrapeRunId = scrapeRun.Id,
                    Items       = chunk,
                });
            }

            logger.LogInformation(
                "CimriPriceWatch: {Count} ürün {Batches} batch halinde kuyruğa alındı (runId={RunId}).",
                items.Count, (int)Math.Ceiling(items.Count / (double)batchSize), scrapeRun.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CimriPriceWatch: kuyruğa atma başarısız (runId={RunId}).", scrapeRun.Id);
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
