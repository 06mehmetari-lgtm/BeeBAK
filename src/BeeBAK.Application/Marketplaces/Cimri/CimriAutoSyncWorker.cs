using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri.Jobs;
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

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Robot 1 — Cimri tarama motoru.
/// Her 30 dakikada bir tüm URL'leri BAĞIMSIZ olarak kontrol eder.
/// Her URL kendi cooldown'ına göre çalışır — biri bitmeden diğeri başlar.
/// Ürün bulunduğunda Telegram'a anında iletilir.
/// </summary>
public class CimriAutoSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string MutexKey          = "cimri:autosync:mutex";
    private const string CooldownKeyPrefix = "beebak:cimri:url:cd:";
    private const string ForceResyncKey    = "beebak:system:force-resync";
    private static readonly TimeSpan StuckRunThreshold = TimeSpan.FromMinutes(20);

    public CimriAutoSyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<CimriClientOptions> options)
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
        var options = sp.GetRequiredService<IOptionsMonitor<CimriClientOptions>>().CurrentValue;

        if (!options.AutoSync.Enabled) return;

        var logger = sp.GetRequiredService<ILogger<CimriAutoSyncWorker>>();

        // Distributed lock — tek replica yönetir
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

        try { await RunSyncAsync(sp, options, logger); }
        finally { if (mutexHandle != null) await mutexHandle.DisposeAsync(); }
    }

    private static async Task RunSyncAsync(
        IServiceProvider sp,
        CimriClientOptions options,
        ILogger<CimriAutoSyncWorker> logger)
    {
        var scrapeRunRepo = sp.GetRequiredService<IRepository<EcScrapeRun, Guid>>();
        var uowManager    = sp.GetRequiredService<IUnitOfWorkManager>();
        var clock         = sp.GetRequiredService<IClock>();
        var cache         = sp.GetRequiredService<IDistributedCache>();

        // Self-heal: takılı run'ları temizle
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
                stuck.Fail(clock.Now, "self-heal: 90 dk+ Running kaldı");
                await scrapeRunRepo.UpdateAsync(stuck, autoSave: true);
                logger.LogWarning("CimriAutoSync: takılı run temizlendi (runId={RunId}).", stuck.Id);
            }
            await uow.CompleteAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "CimriAutoSync: self-heal hatası."); }

        // Her URL bağımsız — kendi cooldown'una göre çalışır
        var allUrls   = GetAllListingUrls(options);
        var cooldownM = options.AutoSync.CategoryIntervalMinutes > 0
            ? options.AutoSync.CategoryIntervalMinutes * 2   // 2 tur bekle = 60 dk
            : (int)TimeSpan.FromHours(options.AutoSync.IntervalHours).TotalMinutes;

        var maxPages    = Math.Max(1, options.AutoSync.MaxPages);
        var maxProducts = Math.Max(1, options.AutoSync.MaxProducts);

        var guidGenerator = sp.GetRequiredService<IGuidGenerator>();
        var jobManager    = sp.GetRequiredService<IBackgroundJobManager>();
        var eventLogger   = sp.GetRequiredService<IScrapeRunEventLogger>();

        // Watchdog force-resync flag'ini kontrol et
        bool forceResync = false;
        try
        {
            var fr = await cache.GetAsync(ForceResyncKey);
            if (fr != null)
            {
                forceResync = true;
                await cache.RemoveAsync(ForceResyncKey);
                logger.LogWarning("CimriAutoSync: force-resync flag bulundu — tüm cooldown'lar atlanıyor.");
            }
        }
        catch { }

        int enqueued = 0;
        foreach (var url in allUrls)
        {
            var cdKey = CooldownKeyPrefix + GetHash(url);

            // Cooldown aktifse bu URL'yi atla (force-resync varsa atla)
            if (!forceResync)
            {
                try
                {
                    var cd = await cache.GetAsync(cdKey);
                    if (cd != null)
                    {
                        logger.LogDebug("CimriAutoSync: {Url} cooldown'da — atlanıyor.", url);
                        continue;
                    }
                }
                catch { /* Redis erişilemezse yine de devam et */ }
            }

            // Cooldown'u hemen kaydet (çift kuyruğa almayı önle)
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
            catch { /* best-effort */ }

            await EnqueueUrlAsync(sp, url, maxPages, maxProducts,
                scrapeRunRepo, uowManager, clock, guidGenerator, jobManager, eventLogger, logger);
            enqueued++;
        }

        if (enqueued > 0)
            logger.LogInformation("CimriAutoSync: {Count}/{Total} URL kuyruğa alındı.", enqueued, allUrls.Count);
        else
            logger.LogDebug("CimriAutoSync: tüm URL'ler cooldown'da — bu tur atlandı.");
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
            logger.LogError(ex, "CimriAutoSync: scrape run oluşturulamadı (url={Url}).", url);
            return;
        }

        try
        {
            await eventLogger.LogAsync(scrapeRun.Id, EcScrapeRunEventLevel.Info, "auto-sync",
                $"Tarama başlatıldı — {url} | {maxPages} sayfa | maks {maxProducts} ürün");

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

            logger.LogInformation("CimriAutoSync: kuyruğa alındı (runId={RunId}, url={Url})", scrapeRun.Id, url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CimriAutoSync: kuyruğa atma hatası (runId={RunId}).", scrapeRun.Id);
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

        return new List<string>
        {
            !string.IsNullOrWhiteSpace(options.AutoSync.ListingPageUrl) ? options.AutoSync.ListingPageUrl
            : !string.IsNullOrWhiteSpace(options.ListingPageUrl)        ? options.ListingPageUrl
            : "https://www.cimri.com/indirimli-urunler"
        };
    }

    private static string GetHash(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
