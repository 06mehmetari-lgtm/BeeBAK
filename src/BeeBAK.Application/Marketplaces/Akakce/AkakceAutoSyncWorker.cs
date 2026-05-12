using System;
using System.Text;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Akakce.Jobs;
using BeeBAK.Marketplaces.Cimri.Logging;
using Medallion.Threading;
using Microsoft.Extensions.Caching.Distributed;
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

public class AkakceAutoSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string MutexKey       = "akakce:autosync:mutex";
    private const string CategoryIdxKey = "beebak:akakce:autosync:catidx";
    private static readonly TimeSpan StuckRunThreshold = TimeSpan.FromMinutes(45);

    public AkakceAutoSyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<AkakceClientOptions> options)
        : base(timer, serviceScopeFactory)
    {
        var autoSync = options.Value.AutoSync;

        if (autoSync.CategoryUrls.Count > 0 && autoSync.CategoryIntervalMinutes > 0)
            Timer.Period = (int)TimeSpan.FromMinutes(autoSync.CategoryIntervalMinutes).TotalMilliseconds;
        else
            Timer.Period = (int)TimeSpan.FromHours(Math.Max(1, autoSync.IntervalHours)).TotalMilliseconds;

        Timer.RunOnStart = autoSync.RunOnStart;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var options = sp.GetRequiredService<IOptionsMonitor<AkakceClientOptions>>().CurrentValue;

        if (!options.AutoSync.Enabled) return;

        var logger = sp.GetRequiredService<ILogger<AkakceAutoSyncWorker>>();

        var lockProvider = sp.GetService<IDistributedLockProvider>();
        IAsyncDisposable? mutexHandle = null;
        if (lockProvider != null)
        {
            mutexHandle = await lockProvider.TryAcquireLockAsync(MutexKey, TimeSpan.Zero);
            if (mutexHandle == null)
            {
                logger.LogInformation("AkakceAutoSync: diğer replica mutex'i tuttu — bu tur atlanıyor.");
                return;
            }
        }

        try
        {
            await RunSyncAsync(sp, options, logger);
        }
        finally
        {
            if (mutexHandle != null) await mutexHandle.DisposeAsync();
        }
    }

    private static async Task RunSyncAsync(
        IServiceProvider sp,
        AkakceClientOptions options,
        ILogger<AkakceAutoSyncWorker> logger)
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
                r.Marketplace == MarketplaceKind.Akakce &&
                r.Status == EcScrapeRunStatus.Running &&
                r.StartedUtc < stuckCutoff);
            foreach (var stuck in stuckRuns)
            {
                stuck.Fail(clock.Now, "self-heal: 45 dakikadan uzun Running kaldı");
                await scrapeRunRepo.UpdateAsync(stuck, autoSave: true);
                logger.LogWarning(
                    "AkakceAutoSync: takılı run self-heal ile Failed yapıldı (runId={RunId}, başladı={Started:HH:mm} UTC).",
                    stuck.Id, stuck.StartedUtc);
            }
            await uow.CompleteAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "AkakceAutoSync: self-heal başarısız, devam ediliyor."); }

        // Aktif tarama var mı?
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            var recentCutoff = DateTime.UtcNow - StuckRunThreshold;
            var activeRuns = await scrapeRunRepo.GetListAsync(r =>
                r.Marketplace == MarketplaceKind.Akakce &&
                r.Status == EcScrapeRunStatus.Running &&
                r.StartedUtc >= recentCutoff);
            await uow.CompleteAsync();
            if (activeRuns.Count > 0)
            {
                logger.LogInformation(
                    "AkakceAutoSync: aktif tarama devam ediyor (runId={RunId}) — bu tur atlanıyor.", activeRuns[0].Id);
                return;
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "AkakceAutoSync: aktif tarama kontrolü başarısız."); }

        // Kategori URL seç (round-robin) veya varsayılan URL kullan
        var listingUrl = await ResolveNextListingUrlAsync(sp, options, logger);

        var maxPages    = Math.Max(1, options.AutoSync.MaxPages);
        var maxProducts = Math.Max(1, options.AutoSync.MaxProducts);

        var guidGenerator = sp.GetRequiredService<IGuidGenerator>();
        var jobManager    = sp.GetRequiredService<IBackgroundJobManager>();

        EcScrapeRun scrapeRun;
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            scrapeRun = new EcScrapeRun(
                guidGenerator.Create(),
                MarketplaceKind.Akakce,
                EcScrapeRunStatus.Running,
                clock.Now,
                triggerSource: nameof(AkakceAutoSyncWorker));
            await scrapeRunRepo.InsertAsync(scrapeRun, autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AkakceAutoSync: tarama kaydı oluşturulamadı.");
            return;
        }

        try
        {
            await jobManager.EnqueueAsync(new AkakceListingDiscoveryJobArgs
            {
                ScrapeRunId    = scrapeRun.Id,
                MaxPages       = maxPages,
                MaxProducts    = maxProducts,
                IncludeOffers  = true,
                ForceRefresh   = false,
                ListingPageUrl = listingUrl,
            });

            logger.LogInformation(
                "AkakceAutoSync: tarama kuyruğa alındı (runId={RunId}, url={Url}, maxPages={P}, maxProducts={Prod})",
                scrapeRun.Id, listingUrl, maxPages, maxProducts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AkakceAutoSync: kuyruğa atma başarısız (runId={RunId}).", scrapeRun.Id);
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

    private static async Task<string> ResolveNextListingUrlAsync(
        IServiceProvider sp,
        AkakceClientOptions options,
        ILogger<AkakceAutoSyncWorker> logger)
    {
        var categories = options.AutoSync.CategoryUrls;
        if (categories.Count == 0)
        {
            return !string.IsNullOrWhiteSpace(options.AutoSync.ListingPageUrl)
                ? options.AutoSync.ListingPageUrl
                : options.ListingPageUrl ?? "https://www.akakce.com/fiyati-dusen-urunler/?s=5";
        }

        var cache = sp.GetRequiredService<IDistributedCache>();
        int currentIdx = 0;
        try
        {
            var raw = await cache.GetAsync(CategoryIdxKey);
            if (raw != null)
                int.TryParse(Encoding.UTF8.GetString(raw), out currentIdx);
        }
        catch (Exception ex) { logger.LogWarning(ex, "AkakceAutoSync: kategori index okunamadı, 0'dan başlanıyor."); }

        var url = categories[currentIdx % categories.Count];
        var nextIdx = currentIdx + 1;

        try
        {
            await cache.SetAsync(
                CategoryIdxKey,
                Encoding.UTF8.GetBytes(nextIdx.ToString()),
                new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(7) });
        }
        catch (Exception ex) { logger.LogWarning(ex, "AkakceAutoSync: kategori index kaydedilemedi."); }

        logger.LogInformation(
            "AkakceAutoSync: kategori seçildi [{Idx}/{Total}] → {Url}",
            currentIdx % categories.Count + 1, categories.Count, url);

        return url;
    }
}
