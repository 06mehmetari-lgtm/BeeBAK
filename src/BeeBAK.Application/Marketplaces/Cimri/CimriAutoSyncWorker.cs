using System;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri.Jobs;
using BeeBAK.Marketplaces.Cimri.Logging;
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
/// Cimri listeleme sayfasını yapılandırılmış aralıklarla otomatik tarayan arka plan çalışanı.
/// Yeni ürünler dedup cache'de yoksa Telegram'a otomatik gönderilir.
/// Hata durumunda bir sonraki aralığa kadar bekleyip yeniden dener — sistem kendiliğinden toparlanır.
/// </summary>
public class CimriAutoSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    public CimriAutoSyncWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<CimriClientOptions> options)
        : base(timer, serviceScopeFactory)
    {
        var autoSync = options.Value.AutoSync;
        Timer.Period = (int)TimeSpan.FromHours(Math.Max(1, autoSync.IntervalHours)).TotalMilliseconds;
        Timer.RunOnStart = autoSync.RunOnStart;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var options = sp.GetRequiredService<IOptionsMonitor<CimriClientOptions>>().CurrentValue;

        if (!options.AutoSync.Enabled)
        {
            return;
        }

        var logger = sp.GetRequiredService<ILogger<CimriAutoSyncWorker>>();
        var scrapeRunRepo = sp.GetRequiredService<IRepository<EcScrapeRun, Guid>>();
        var uowManager = sp.GetRequiredService<IUnitOfWorkManager>();

        // Çakışan aktif taramayı önle
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            var cutoff = DateTime.UtcNow.AddHours(-6);
            var activeRuns = await scrapeRunRepo.GetListAsync(r =>
                r.Marketplace == MarketplaceKind.Cimri &&
                r.Status == EcScrapeRunStatus.Running &&
                r.StartedUtc > cutoff);
            await uow.CompleteAsync();

            if (activeRuns.Count > 0)
            {
                logger.LogInformation(
                    "CimriAutoSync: aktif tarama zaten devam ediyor (runId={RunId}, başladı={Started:HH:mm} UTC) — bu tur atlanıyor.",
                    activeRuns[0].Id, activeRuns[0].StartedUtc);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CimriAutoSync: aktif tarama kontrolü başarısız, yine de devam ediliyor.");
        }

        var listingUrl = !string.IsNullOrWhiteSpace(options.AutoSync.ListingPageUrl)
            ? options.AutoSync.ListingPageUrl
            : !string.IsNullOrWhiteSpace(options.ListingPageUrl)
                ? options.ListingPageUrl
                : "https://www.cimri.com/indirimli-urunler";

        var maxPages = Math.Max(1, options.AutoSync.MaxPages);
        var maxProducts = Math.Max(1, options.AutoSync.MaxProducts);

        var guidGenerator = sp.GetRequiredService<IGuidGenerator>();
        var clock = sp.GetRequiredService<IClock>();
        var jobManager = sp.GetRequiredService<IBackgroundJobManager>();
        var eventLogger = sp.GetRequiredService<IScrapeRunEventLogger>();

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
            logger.LogError(ex, "CimriAutoSync: tarama kaydı oluşturulamadı — bir sonraki aralıkta tekrar denecek.");
            return;
        }

        try
        {
            await eventLogger.LogAsync(
                scrapeRun.Id, EcScrapeRunEventLevel.Info, "auto-sync",
                $"Otomatik tarama başlatıldı — {listingUrl} | {maxPages} sayfa | maks {maxProducts} ürün | {options.AutoSync.IntervalHours}h aralık.");

            await jobManager.EnqueueAsync(new CimriListingDiscoveryJobArgs
            {
                ScrapeRunId = scrapeRun.Id,
                MaxPages = maxPages,
                MaxProducts = maxProducts,
                IncludeOffers = true,
                ExpandAllOffers = true,
                ForceRefresh = false,
                ListingPageUrl = listingUrl,
            });

            logger.LogInformation(
                "CimriAutoSync: tarama kuyruğa alındı (runId={RunId}, url={Url}, maxPages={P}, maxProducts={Prod})",
                scrapeRun.Id, listingUrl, maxPages, maxProducts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CimriAutoSync: kuyruğa atma başarısız (runId={RunId}) — bir sonraki aralıkta tekrar denecek.", scrapeRun.Id);
            try
            {
                using var uow = uowManager.Begin(requiresNew: true);
                var run = await scrapeRunRepo.FindAsync(scrapeRun.Id);
                if (run != null)
                {
                    run.Fail(clock.Now, ex.Message);
                    await scrapeRunRepo.UpdateAsync(run, autoSave: true);
                }
                await uow.CompleteAsync();
            }
            catch { /* best-effort */ }
        }
    }
}
