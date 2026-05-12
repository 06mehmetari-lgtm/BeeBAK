using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri.Jobs;
using BeeBAK.Marketplaces.Cimri.Logging;
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
/// Robot 1 — Cimri tarama motoru.
/// Her tur tüm yapılandırılmış kategori URL'lerini AYNI ANDA kuyruğa alır.
/// Önceki tur henüz bitmemişse (aktif run varsa) bu tur atlanır.
/// </summary>
public class CimriAutoSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string MutexKey = "cimri:autosync:mutex";

    // Takılı run eşiği: bir listing discovery + 1000 ürün detay işi en fazla 90 dk sürebilir
    private static readonly TimeSpan StuckRunThreshold  = TimeSpan.FromMinutes(90);
    private static readonly TimeSpan ActiveRunThreshold = TimeSpan.FromMinutes(90);

    public CimriAutoSyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<CimriClientOptions> options)
        : base(timer, serviceScopeFactory)
    {
        var autoSync = options.Value.AutoSync;

        // Kategori listesi varsa CategoryIntervalMinutes, yoksa IntervalHours
        if (autoSync.CategoryUrls.Count > 0 && autoSync.CategoryIntervalMinutes > 0)
            Timer.Period = (int)TimeSpan.FromMinutes(autoSync.CategoryIntervalMinutes).TotalMilliseconds;
        else
            Timer.Period = (int)TimeSpan.FromHours(Math.Max(1, autoSync.IntervalHours)).TotalMilliseconds;

        Timer.RunOnStart = autoSync.RunOnStart;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var options = sp.GetRequiredService<IOptionsMonitor<CimriClientOptions>>().CurrentValue;

        if (!options.AutoSync.Enabled)
            return;

        var logger = sp.GetRequiredService<ILogger<CimriAutoSyncWorker>>();

        // Distributed lock — tek replica bu turu yönetir
        var lockProvider = sp.GetService<IDistributedLockProvider>();
        IAsyncDisposable? mutexHandle = null;
        if (lockProvider != null)
        {
            mutexHandle = await lockProvider.TryAcquireLockAsync(MutexKey, TimeSpan.Zero);
            if (mutexHandle == null)
            {
                logger.LogInformation("CimriAutoSync: diğer replica mutex'i tuttu — bu tur atlanıyor.");
                return;
            }
        }

        try
        {
            await RunSyncAsync(sp, options, logger);
        }
        finally
        {
            if (mutexHandle != null)
                await mutexHandle.DisposeAsync();
        }
    }

    private static async Task RunSyncAsync(
        IServiceProvider sp,
        CimriClientOptions options,
        ILogger<CimriAutoSyncWorker> logger)
    {
        var scrapeRunRepo = sp.GetRequiredService<IRepository<EcScrapeRun, Guid>>();
        var uowManager    = sp.GetRequiredService<IUnitOfWorkManager>();
        var clock         = sp.GetRequiredService<IClock>();

        // Takılı run self-heal
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            var stuckCutoff = DateTime.UtcNow - StuckRunThreshold;
            var stuckRuns = await scrapeRunRepo.GetListAsync(r =>
                r.Marketplace == MarketplaceKind.Cimri &&
                r.Status == EcScrapeRunStatus.Running &&
                r.StartedUtc < stuckCutoff);

            foreach (var stuck in stuckRuns)
            {
                stuck.Fail(clock.Now, "self-heal: 90 dakikadan uzun Running kaldı");
                await scrapeRunRepo.UpdateAsync(stuck, autoSave: true);
                logger.LogWarning(
                    "CimriAutoSync: takılı run self-heal ile Failed yapıldı (runId={RunId}, başladı={Started:HH:mm} UTC).",
                    stuck.Id, stuck.StartedUtc);
            }

            await uow.CompleteAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "CimriAutoSync: self-heal başarısız, devam ediliyor."); }

        // Aktif tarama var mı? Varsa bu turu atla (tüm URL'ler bitince yeniden başla)
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            var recentCutoff = DateTime.UtcNow - ActiveRunThreshold;
            var activeRuns = await scrapeRunRepo.GetListAsync(r =>
                r.Marketplace == MarketplaceKind.Cimri &&
                r.Status == EcScrapeRunStatus.Running &&
                r.StartedUtc >= recentCutoff);
            await uow.CompleteAsync();

            if (activeRuns.Count > 0)
            {
                logger.LogInformation(
                    "CimriAutoSync: {Count} aktif tarama devam ediyor — bu tur atlanıyor.", activeRuns.Count);
                return;
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "CimriAutoSync: aktif tarama kontrolü başarısız, yine de devam ediliyor."); }

        // Taranacak tüm URL'leri al
        var allUrls = GetAllListingUrls(options);
        var maxPages    = Math.Max(1, options.AutoSync.MaxPages);
        var maxProducts = Math.Max(1, options.AutoSync.MaxProducts);

        var guidGenerator = sp.GetRequiredService<IGuidGenerator>();
        var jobManager    = sp.GetRequiredService<IBackgroundJobManager>();
        var eventLogger   = sp.GetRequiredService<IScrapeRunEventLogger>();

        logger.LogInformation(
            "CimriAutoSync: {Count} URL aynı anda kuyruğa alınıyor (maxPages={P}, maxProducts={Prod}).",
            allUrls.Count, maxPages, maxProducts);

        // Her URL için ayrı scrape run + job kuyruğa al
        foreach (var url in allUrls)
        {
            await EnqueueUrlAsync(sp, url, maxPages, maxProducts,
                scrapeRunRepo, uowManager, clock, guidGenerator, jobManager, eventLogger, logger);
        }
    }

    private static async Task EnqueueUrlAsync(
        IServiceProvider sp,
        string url,
        int maxPages,
        int maxProducts,
        IRepository<EcScrapeRun, Guid> scrapeRunRepo,
        IUnitOfWorkManager uowManager,
        IClock clock,
        IGuidGenerator guidGenerator,
        IBackgroundJobManager jobManager,
        IScrapeRunEventLogger eventLogger,
        ILogger<CimriAutoSyncWorker> logger)
    {
        EcScrapeRun scrapeRun;
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            scrapeRun = new EcScrapeRun(
                guidGenerator.Create(),
                MarketplaceKind.Cimri,
                EcScrapeRunStatus.Running,
                clock.Now,
                triggerSource: nameof(CimriAutoSyncWorker));
            await scrapeRunRepo.InsertAsync(scrapeRun, autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CimriAutoSync: tarama kaydı oluşturulamadı (url={Url}).", url);
            return;
        }

        try
        {
            await eventLogger.LogAsync(
                scrapeRun.Id, EcScrapeRunEventLevel.Info, "auto-sync",
                $"Otomatik tarama başlatıldı — {url} | {maxPages} sayfa | maks {maxProducts} ürün");

            await jobManager.EnqueueAsync(new CimriListingDiscoveryJobArgs
            {
                ScrapeRunId     = scrapeRun.Id,
                MaxPages        = maxPages,
                MaxProducts     = maxProducts,
                IncludeOffers   = true,
                ExpandAllOffers = true,
                ForceRefresh    = false,
                ListingPageUrl  = url,
            });

            logger.LogInformation(
                "CimriAutoSync: kuyruğa alındı (runId={RunId}, url={Url})", scrapeRun.Id, url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CimriAutoSync: kuyruğa atma başarısız (runId={RunId}, url={Url}).", scrapeRun.Id, url);
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

    private static List<string> GetAllListingUrls(CimriClientOptions options)
    {
        if (options.AutoSync.CategoryUrls.Count > 0)
            return options.AutoSync.CategoryUrls;

        var single = !string.IsNullOrWhiteSpace(options.AutoSync.ListingPageUrl)
            ? options.AutoSync.ListingPageUrl
            : !string.IsNullOrWhiteSpace(options.ListingPageUrl)
                ? options.ListingPageUrl
                : "https://www.cimri.com/indirimli-urunler";

        return new List<string> { single };
    }
}
