using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace BeeBAK;

/// <summary>
/// Robot 3 — Sistem sağlığı izleme ve otomatik iyileştirme çalışanı.
///
/// Her 5 dakikada bir:
///  • Son Telegram gönderimini kontrol eder — 3+ saat sessizse UYARI loglar
///  • Takılı (>45 dk Running) EcScrapeRun kayıtlarını Failed yapar
///  • Aktif scrape run sayısını loglar
///  • Worker heartbeat kaydı tutar (Redis)
/// </summary>
public class SystemHealthWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string HeartbeatKey    = "beebak:system:health:heartbeat";
    private const string LastSendKeyTg   = "beebak:tg:last-send-unix";
    private static readonly TimeSpan StuckRunThreshold   = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan SilenceAlertThreshold = TimeSpan.FromHours(3);

    public SystemHealthWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<SystemHealthWorker>>();

        try
        {
            await RunHealthCheckAsync(sp, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SystemHealth: sağlık kontrolü sırasında hata oluştu.");
        }
    }

    private static async Task RunHealthCheckAsync(IServiceProvider sp, ILogger<SystemHealthWorker> logger)
    {
        var uowManager    = sp.GetRequiredService<IUnitOfWorkManager>();
        var scrapeRunRepo = sp.GetRequiredService<IRepository<EcScrapeRun, Guid>>();
        var cache         = sp.GetRequiredService<IDistributedCache>();

        // Heartbeat kaydet
        try
        {
            await cache.SetAsync(
                HeartbeatKey,
                Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) });
        }
        catch { /* Redis yoksa devam et */ }

        // Telegram sessizlik kontrolü
        try
        {
            var raw = await cache.GetAsync(LastSendKeyTg);
            if (raw != null && long.TryParse(Encoding.UTF8.GetString(raw), out var lastSendUnix))
            {
                var lastSend = DateTimeOffset.FromUnixTimeSeconds(lastSendUnix).UtcDateTime;
                var silence  = DateTime.UtcNow - lastSend;
                if (silence > SilenceAlertThreshold)
                    logger.LogWarning(
                        "SystemHealth: UYARI Telegram'a son gönderi {Minutes:0} dakika önce yapıldı — sistem sessiz olabilir!",
                        silence.TotalMinutes);
                else
                    logger.LogDebug("SystemHealth: Telegram son gönderi {Minutes:0} dk önce.", silence.TotalMinutes);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "SystemHealth: Telegram sessizlik kontrolü başarısız."); }

        // Aktif ve takılı run istatistiği (yalnız okuma — self-heal AutoSync worker'lara bırakılmış)
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            var stuckCutoff = DateTime.UtcNow - StuckRunThreshold;
            var activeRuns  = await scrapeRunRepo.GetListAsync(r => r.Status == EcScrapeRunStatus.Running);
            var stuckCount  = activeRuns.Count(r => r.StartedUtc < stuckCutoff);
            await uow.CompleteAsync();

            if (stuckCount > 0)
                logger.LogWarning(
                    "SystemHealth: {Stuck} takılı run var (AutoSync worker sonraki turda düzeltecek), aktif={Active}, zaman={Time:HH:mm} UTC",
                    stuckCount, activeRuns.Count, DateTime.UtcNow);
            else
                logger.LogInformation(
                    "SystemHealth: aktif run={Active}, zaman={Time:HH:mm} UTC",
                    activeRuns.Count, DateTime.UtcNow);
        }
        catch (Exception ex) { logger.LogWarning(ex, "SystemHealth: istatistik toplanamadı."); }
    }
}
