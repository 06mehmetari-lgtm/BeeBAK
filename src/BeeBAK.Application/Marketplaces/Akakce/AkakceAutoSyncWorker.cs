using System;
using System.Collections.Generic;
using System.Security.Cryptography;
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

/// <summary>
/// Robot 1 — Akakçe tarama motoru.
/// Her URL bağımsız cooldown'la çalışır. Biri bitmeden diğeri başlar.
/// </summary>
public class AkakceAutoSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string MutexKey          = "akakce:autosync:mutex";
    private const string CooldownKeyPrefix = "beebak:akakce:url:cd:";
    private const string ForceResyncKey    = "beebak:system:force-resync";
    private static readonly TimeSpan StuckRunThreshold = TimeSpan.FromMinutes(20);

    public AkakceAutoSyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<AkakceClientOptions> options)
        : base(timer, serviceScopeFactory)
    {
        var autoSync = options.Value.AutoSync;
        Timer.Period = (int)TimeSpan.FromMinutes(
            autoSync.CategoryIntervalMinutes > 0 ? autoSync.CategoryIntervalMinutes : 30
        ).TotalMilliseconds;
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

        try { await RunSyncAsync(sp, options, logger); }
        finally { if (mutexHandle != null) await mutexHandle.DisposeAsync(); }
    }

    private static async Task RunSyncAsync(
        IServiceProvider sp,
        AkakceClientOptions options,
        ILogger<AkakceAutoSyncWorker> logger)
    {
        var scrapeRunRepo = sp.GetRequiredService<IRepository<EcScrapeRun, Guid>>();
        var uowManager    = sp.GetRequiredService<IUnitOfWorkManager>();
        var clock         = sp.GetRequiredService<IClock>();
        var cache         = sp.GetRequiredService<IDistributedCache>();

        // Self-heal
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
                stuck.Fail(clock.Now, "self-heal: 90 dk+ Running kaldı");
                await scrapeRunRepo.UpdateAsync(stuck, autoSave: true);
                logger.LogWarning("AkakceAutoSync: takılı run temizlendi (runId={RunId}).", stuck.Id);
            }
            await uow.CompleteAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "AkakceAutoSync: self-heal hatası."); }

        var allUrls   = GetAllListingUrls(options);
        var cooldownM = options.AutoSync.CategoryIntervalMinutes > 0
            ? options.AutoSync.CategoryIntervalMinutes * 2
            : (int)TimeSpan.FromHours(options.AutoSync.IntervalHours).TotalMinutes;

        var maxPages    = Math.Max(1, options.AutoSync.MaxPages);
        var maxProducts = Math.Max(1, options.AutoSync.MaxProducts);

        var guidGenerator = sp.GetRequiredService<IGuidGenerator>();
        var jobManager    = sp.GetRequiredService<IBackgroundJobManager>();

        // Watchdog force-resync flag'ini kontrol et
        bool forceResync = false;
        try
        {
            var fr = await cache.GetAsync(ForceResyncKey);
            if (fr != null)
            {
                forceResync = true;
                await cache.RemoveAsync(ForceResyncKey);
                logger.LogWarning("AkakceAutoSync: force-resync flag bulundu — tüm cooldown'lar atlanıyor.");
            }
        }
        catch { }

        int enqueued = 0;
        foreach (var url in allUrls)
        {
            var cdKey = CooldownKeyPrefix + GetHash(url);

            if (!forceResync)
            {
                try
                {
                    var cd = await cache.GetAsync(cdKey);
                    if (cd != null)
                    {
                        logger.LogDebug("AkakceAutoSync: {Url} cooldown'da — atlanıyor.", url);
                        continue;
                    }
                }
                catch { }
            }

            try
            {
                await cache.SetAsync(
                    cdKey,
                    Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(cooldownM)
                    });
            }
            catch { }

            await EnqueueUrlAsync(sp, url, maxPages, maxProducts,
                scrapeRunRepo, uowManager, clock, guidGenerator, jobManager, logger);
            enqueued++;
        }

        if (enqueued > 0)
            logger.LogInformation("AkakceAutoSync: {Count}/{Total} URL kuyruğa alındı.", enqueued, allUrls.Count);
        else
            logger.LogDebug("AkakceAutoSync: tüm URL'ler cooldown'da — bu tur atlandı.");
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
        ILogger<AkakceAutoSyncWorker> logger)
    {
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
            logger.LogError(ex, "AkakceAutoSync: scrape run oluşturulamadı (url={Url}).", url);
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
                ListingPageUrl = url,
            });

            logger.LogInformation("AkakceAutoSync: kuyruğa alındı (runId={RunId}, url={Url})", scrapeRun.Id, url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AkakceAutoSync: kuyruğa atma hatası (runId={RunId}).", scrapeRun.Id);
            try
            {
                using var uow = uowManager.Begin(requiresNew: true);
                var run = await scrapeRunRepo.FindAsync(scrapeRun.Id);
                if (run != null) { run.Fail(clock.Now, ex.Message); await scrapeRunRepo.UpdateAsync(run, autoSave: true); }
                await uow.CompleteAsync();
            }
            catch { }
        }
    }

    private static List<string> GetAllListingUrls(AkakceClientOptions options)
    {
        if (options.AutoSync.CategoryUrls.Count > 0)
            return options.AutoSync.CategoryUrls;

        return new List<string>
        {
            !string.IsNullOrWhiteSpace(options.AutoSync.ListingPageUrl) ? options.AutoSync.ListingPageUrl
            : options.ListingPageUrl ?? "https://www.akakce.com/fiyati-dusen-urunler/?s=5"
        };
    }

    private static string GetHash(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
