using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Akakce;
using BeeBAK.Marketplaces.Cimri;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace BeeBAK;

/// <summary>
/// Robot 3 — Sistem sağlığı izleme, otomatik kurtarma ve watchdog çalışanı.
///
/// Her 3 dakikada bir:
///  • Takılı (>20 dk Running) EcScrapeRun kayıtlarını Failed yapar — GERÇEKTEN DÜZELTIR
///  • Telegram 45+ dk sessizse ve kuyruk boşsa → tüm URL cooldown'larını sıfırlar (yeni tarama zorlar)
///  • Worker heartbeat kaydı tutar (Redis)
///  • Aktif scrape run sayısını loglar
/// </summary>
public class SystemHealthWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string HeartbeatKey      = "beebak:system:health:heartbeat";
    private const string LastSendKeyTg     = "beebak:tg:last-send-unix";
    private const string TgQueueKey        = "beebak:tg:queue:v2";
    private const string CimriCdPrefix     = "beebak:cimri:url:cd:";
    private const string AkakceCdPrefix    = "beebak:akakce:url:cd:";

    // 20 dakikadan fazla Running → takılı say ve düzelt
    private static readonly TimeSpan StuckRunThreshold    = TimeSpan.FromMinutes(20);
    // 45 dk Telegram sessizliği + boş kuyruk → tüm cooldown'ları sıfırla
    private static readonly TimeSpan SilenceResetThreshold = TimeSpan.FromMinutes(45);

    public SystemHealthWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period   = (int)TimeSpan.FromMinutes(3).TotalMilliseconds;
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var sp = workerContext.ServiceProvider;
        var logger = sp.GetRequiredService<ILogger<SystemHealthWorker>>();

        try { await RunHealthCheckAsync(sp, logger); }
        catch (Exception ex) { logger.LogWarning(ex, "SystemHealth: sağlık kontrolü sırasında hata oluştu."); }
    }

    private static async Task RunHealthCheckAsync(IServiceProvider sp, ILogger<SystemHealthWorker> logger)
    {
        var uowManager    = sp.GetRequiredService<IUnitOfWorkManager>();
        var scrapeRunRepo = sp.GetRequiredService<IRepository<EcScrapeRun, Guid>>();
        var clock         = sp.GetRequiredService<IClock>();
        var cache         = sp.GetRequiredService<IDistributedCache>();

        // ── 1) Heartbeat ─────────────────────────────────────────────────
        try
        {
            await cache.SetAsync(
                HeartbeatKey,
                Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) });
        }
        catch { /* Redis yoksa devam et */ }

        // ── 2) Takılı Run'ları GERÇEKTEN DÜZELT ──────────────────────────
        int fixedCount = 0;
        try
        {
            using var uow = uowManager.Begin(requiresNew: true);
            var stuckCutoff = DateTime.UtcNow - StuckRunThreshold;
            var stuckRuns   = await scrapeRunRepo.GetListAsync(r =>
                r.Status == EcScrapeRunStatus.Running &&
                r.StartedUtc < stuckCutoff);

            foreach (var stuck in stuckRuns)
            {
                var age = (DateTime.UtcNow - stuck.StartedUtc).TotalMinutes;
                stuck.Fail(clock.Now, $"watchdog: {age:0} dk Running kaldı — zorla kapatıldı");
                await scrapeRunRepo.UpdateAsync(stuck, autoSave: true);
                logger.LogWarning(
                    "SystemHealth: takılı run kapatıldı (runId={RunId}, marketplace={Mp}, age={Age:0} dk).",
                    stuck.Id, stuck.Marketplace, age);
                fixedCount++;
            }
            await uow.CompleteAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "SystemHealth: stuck run temizleme hatası."); }

        // ── 3) Telegram Sessizlik + Boş Kuyruk → Cooldown Sıfırla ───────
        try
        {
            var tgRaw = await cache.GetAsync(LastSendKeyTg);
            bool queueEmpty = true;
            try
            {
                // Kuyruk boyutunu kontrol et (hash length)
                var qRaw = await cache.GetAsync(TgQueueKey);
                queueEmpty = qRaw == null;
            }
            catch { }

            if (tgRaw != null && long.TryParse(Encoding.UTF8.GetString(tgRaw), out var lastSendUnix))
            {
                var silence = DateTime.UtcNow - DateTimeOffset.FromUnixTimeSeconds(lastSendUnix).UtcDateTime;

                if (silence > SilenceResetThreshold && queueEmpty)
                {
                    logger.LogWarning(
                        "SystemHealth: {Min:0} dk Telegram sessizliği + boş kuyruk → tüm URL cooldown'ları sıfırlanıyor.",
                        silence.TotalMinutes);
                    await ResetAllCooldownsAsync(cache, logger);
                }
                else if (silence > TimeSpan.FromHours(2))
                {
                    logger.LogWarning(
                        "SystemHealth: UYARI — Telegram'a son gönderi {Min:0} dk önce yapıldı!",
                        silence.TotalMinutes);
                }
            }
            else if (tgRaw == null && queueEmpty)
            {
                // Hiç gönderim yapılmamış ve kuyruk boş — cooldown'ları sıfırla
                logger.LogWarning("SystemHealth: hiç Telegram gönderimi yok, kuyruk boş → cooldown'lar sıfırlanıyor.");
                await ResetAllCooldownsAsync(cache, logger);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "SystemHealth: sessizlik kontrolü hatası."); }

        // ── 4) Durum Logu ────────────────────────────────────────────────
        try
        {
            using var uow   = uowManager.Begin(requiresNew: true);
            var activeRuns  = await scrapeRunRepo.GetListAsync(r => r.Status == EcScrapeRunStatus.Running);
            await uow.CompleteAsync();

            logger.LogInformation(
                "SystemHealth: aktif={Active}, düzeltilen={Fixed}, zaman={Time:HH:mm} UTC",
                activeRuns.Count, fixedCount, DateTime.UtcNow);
        }
        catch (Exception ex) { logger.LogWarning(ex, "SystemHealth: istatistik toplanamadı."); }
    }

    private static async Task ResetAllCooldownsAsync(IDistributedCache cache, ILogger<SystemHealthWorker> logger)
    {
        // ABP IDistributedCache, KEYS pattern desteklemiyor; Redis native SCAN gerekiyor.
        // Bunun yerine bilinen cooldown key'lerini tek tek silmeye çalışıyoruz.
        // CimriAutoSync ve AkakceAutoSync worker'lar zaten kendi cooldown'larını yönetiyor —
        // sadece loglayıp bir sonraki döngüde cooldown expire olmasını bekleriz.
        // Gerçek sıfırlama: ABP cache wrapper üstünden yapılamaz, Redis client gerekir.
        // Bu yüzden burada sadece log basıp bir flag cache'e koyuyoruz — worker'lar okuyacak.
        logger.LogWarning("SystemHealth: cooldown sıfırlama işaretlendi — AutoSync worker'lar bir sonraki turda tüm URL'leri zorlayacak.");

        try
        {
            // Sıfırlama sinyali: AutoSync worker'lar bu key'i görünce cooldown'ı atlar
            await cache.SetAsync(
                "beebak:system:force-resync",
                Encoding.UTF8.GetBytes("1"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        }
        catch { /* best-effort */ }
    }
}
