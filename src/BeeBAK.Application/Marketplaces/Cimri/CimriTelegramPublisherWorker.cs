using System;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces;
using BeeBAK.Marketplaces.Monitor;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Akıllı Telegram yayın motoru.
///
/// Her 90 saniyede bir uyanır ve şu kuralları uygular:
///  • Sessiz saat (02-08 İstanbul): sadece yüksek öncelikli (≥100 puan) ürünler gönderilir
///  • Son gönderiyle arası: kuyruk boyutuna göre 3–15 dk random bekleme
///  • Fingerprint kontrolü: fiyat/indirim değişmemişse tekrar gönderilmez
///  • Format rotasyonu: 🔥💸⚡🛒 trigger'a göre başlık değişir
///  • Aynı mağaza üst üste gönderilmez
/// </summary>
public class CimriTelegramPublisherWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string LastSendKey       = "beebak:tg:last-send-unix";
    private const string LastMerchantKey   = "beebak:tg:last-merchant";
    private const string FingerprintPrefix = "beebak:tg:fp:";

    private static readonly Random Rng = new();

    public CimriTelegramPublisherWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = 90_000; // 90 saniye
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext ctx)
    {
        ILogger<CimriTelegramPublisherWorker>? logger = null;
        try
        {
            await RunAsync(ctx);
        }
        catch (Exception ex)
        {
            try
            {
                logger = ctx.ServiceProvider.GetRequiredService<ILogger<CimriTelegramPublisherWorker>>();
                logger.LogError(ex, "Telegram publisher worker beklenmedik hata");
            }
            catch { /* loglama da başarısız → sessizce geç */ }
        }
    }

    private async Task RunAsync(PeriodicBackgroundWorkerContext ctx)
    {
        var sp      = ctx.ServiceProvider;
        var options = sp.GetRequiredService<IOptionsMonitor<CimriClientOptions>>().CurrentValue;
        var pub     = options.Publish;

        if (!options.Telegram.ShareProductCardsOnIngest) return;
        if (string.IsNullOrWhiteSpace(options.Telegram.BotToken)) return;

        var queue  = sp.GetRequiredService<CimriTelegramPublishQueue>();
        var cache  = sp.GetRequiredService<IDistributedCache>();
        var logger = sp.GetRequiredService<ILogger<CimriTelegramPublisherWorker>>();
        var sender = sp.GetRequiredService<ICimriTelegramProductCardSender>();

        // ── 1. Sessiz saat kontrolü (UTC+3 = İstanbul, tzdata gerektirmeden) ──
        var istanbulHour = (DateTime.UtcNow.Hour + 3) % 24;
        var isQuiet      = istanbulHour >= pub.QuietStartHour && istanbulHour < pub.QuietEndHour;

        var queueSize = await queue.GetSizeAsync();

        if (isQuiet && queueSize < 5)
        {
            // Sessiz saatte az ürün varsa bekle
            return;
        }

        // ── 2. Son gönderimden bu yana gerekli minimum süre ─────────────
        var requiredDelaySeconds = ComputeRequiredDelaySeconds(queueSize, pub, isQuiet);
        var lastSendStr = await cache.GetStringAsync(LastSendKey);
        if (!string.IsNullOrEmpty(lastSendStr) &&
            long.TryParse(lastSendStr, out var lastSendUnix))
        {
            var secondsSince = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastSendUnix;
            if (secondsSince < requiredDelaySeconds)
            {
                return;
            }
        }

        // ── 3. Son gönderilen mağazayı oku, farklı mağazayı tercih et ────
        var lastMerchant = await cache.GetStringAsync(LastMerchantKey);

        // ── 4. Kuyruğun tepesinden aday al (son mağazadan kaçın) ─────────
        var entry = await queue.DequeueTopAsync(avoidMerchant: lastMerchant);
        if (entry == null) return;

        // Sessiz saatte sadece yüksek öncelikli ürünleri gönder
        if (isQuiet && entry.Score < 100)
        {
            // Düşük öncelikliyi geri koy, beklet
            await queue.EnqueueAsync(entry);
            return;
        }

        // ── 4. Fingerprint kontrolü (aynı fiyat/indirim → tekrar gönderme) ──
        var currentFp  = CimriProductScorer.BuildFingerprint(entry.LowestPrice, entry.DiscountPercent);
        var storedFp   = await cache.GetStringAsync(FingerprintPrefix + entry.ContentId);
        if (!string.IsNullOrEmpty(storedFp) && storedFp == currentFp)
        {
            logger.LogDebug(
                "Telegram publisher: fingerprint aynı, atlanıyor ({ContentId})", entry.ContentId);
            return;
        }

        // ── 5. Aynı mağaza üst üste engellemesi ─────────────────────────
        // (Bu kontrol sender seviyesinde değil, burada merchant adı yok;
        //  sender kendisi DB'den okur. Basit koruma: last-merchant string'i karşılaştır)
        // Not: entry'de merchant bilgisi yok, bu kontrolü sender'da yapıyoruz.

        // ── 6. Gönder ────────────────────────────────────────────────────
        logger.LogInformation(
            "Telegram publisher: gönderiliyor ({ContentId}, trigger={Trigger}, score={Score:F0}, queueSize={QSize})",
            entry.ContentId, entry.TriggerType, entry.Score, queueSize);

        try
        {
            await sender.TrySendAfterProductIngestedAsync(
                entry.ContentId,
                entry.TriggerType,
                cancellationToken: default);

            // ── 7. Başarı → fingerprint + last-send güncelle ─────────────
            await cache.SetStringAsync(
                FingerprintPrefix + entry.ContentId,
                currentFp,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7),
                });

            await cache.SetStringAsync(
                LastSendKey,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
                });

            // Son gönderilen mağazayı kaydet (bir sonraki gönderimde farklı mağaza seçilsin)
            if (!string.IsNullOrEmpty(entry.MerchantName))
            {
                await cache.SetStringAsync(
                    LastMerchantKey,
                    entry.MerchantName,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
                    });
            }

            // ── 8. Canlı izleme geçmişine kaydet ─────────────────────────
            try
            {
                var history = sp.GetRequiredService<TelegramSentHistory>();
                var prodRepo = sp.GetRequiredService<IRepository<CimriProduct, Guid>>();
                var prods = await prodRepo.GetListAsync(p => p.ContentId == entry.ContentId);
                var prod  = prods.FirstOrDefault();
                await history.AddAsync(new TelegramSentItemDto
                {
                    Id            = entry.ContentId,
                    Marketplace   = MarketplaceKind.Cimri,
                    Title         = prod?.Title ?? entry.ContentId,
                    ImageUrl      = prod?.PrimaryImageUrl,
                    ProductUrl    = prod?.ProductUrl ?? "",
                    Price         = entry.LowestPrice,
                    PreviousPrice = entry.PreviousPrice,
                    DiscountPercent = entry.DiscountPercent,
                    TriggerType   = entry.TriggerType,
                    SentAt        = DateTimeOffset.UtcNow,
                });
            }
            catch { /* best-effort */ }

            logger.LogInformation(
                "Telegram publisher: gönderildi ✓ ({ContentId})", entry.ContentId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Telegram publisher: gönderim başarısız ({ContentId})", entry.ContentId);
        }
    }  // end RunAsync

    // ── Kuyruk boyutuna ve saate göre bekleme süresi (saniye) ────────────
    // Prime time 18:00-01:00 İstanbul → daha sık paylaşım
    private static int ComputeRequiredDelaySeconds(
        int queueSize, CimriPublishOptions pub, bool isQuiet)
    {
        int minSec, maxSec;

        if (isQuiet)
        {
            minSec = 25 * 60;
            maxSec = 35 * 60;
        }
        else
        {
            var istHour    = (DateTime.UtcNow.Hour + 3) % 24;
            var isPrimeTime = istHour >= 18 || istHour < 1; // 18:00-01:00

            if (isPrimeTime)
            {
                // Prime time: hızlı gönder — kuyruk ne kadar doluysa o kadar hızlı
                if (queueSize >= 20)
                {
                    minSec = pub.MinDelayMinutes * 60;
                    maxSec = (pub.MinDelayMinutes + 2) * 60;
                }
                else if (queueSize >= 5)
                {
                    minSec = pub.MinDelayMinutes * 60;
                    maxSec = (int)((pub.MinDelayMinutes + pub.MaxDelayMinutes) * 0.5 * 60);
                }
                else
                {
                    minSec = pub.MinDelayMinutes * 60;
                    maxSec = pub.MaxDelayMinutes * 60;
                }
            }
            else
            {
                // Gündüz / gece arası — normal hız
                if (queueSize >= 20)
                {
                    minSec = pub.MinDelayMinutes * 60;
                    maxSec = (pub.MinDelayMinutes + 3) * 60;
                }
                else if (queueSize >= 10)
                {
                    minSec = pub.MinDelayMinutes * 60;
                    maxSec = (int)((pub.MinDelayMinutes + pub.MaxDelayMinutes) * 0.5 * 60);
                }
                else
                {
                    minSec = (int)((pub.MinDelayMinutes + pub.MaxDelayMinutes) * 0.5 * 60);
                    maxSec = pub.MaxDelayMinutes * 60;
                }
            }
        }

        return Rng.Next(minSec, maxSec + 1);
    }
}
