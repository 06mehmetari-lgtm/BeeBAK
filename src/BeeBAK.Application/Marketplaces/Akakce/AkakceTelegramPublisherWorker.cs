using System;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Marketplaces.Monitor;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceTelegramPublisherWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const string LastSendKey       = "beebak:tg:akakce:last-send-unix";
    private const string LastMerchantKey   = "beebak:tg:akakce:last-merchant";
    private const string FingerprintPrefix = "beebak:tg:akakce:fp:";

    private static readonly Random Rng = new();

    public AkakceTelegramPublisherWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = 90_000;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext ctx)
    {
        try { await RunAsync(ctx); }
        catch (Exception ex)
        {
            try
            {
                ctx.ServiceProvider
                   .GetRequiredService<ILogger<AkakceTelegramPublisherWorker>>()
                   .LogError(ex, "Akakce Telegram publisher worker beklenmedik hata");
            }
            catch { }
        }
    }

    private async Task RunAsync(PeriodicBackgroundWorkerContext ctx)
    {
        var sp      = ctx.ServiceProvider;
        var telegram = sp.GetRequiredService<IOptionsMonitor<CimriClientOptions>>().CurrentValue.Telegram;
        var akakce  = sp.GetRequiredService<IOptionsMonitor<AkakceClientOptions>>().CurrentValue;
        var pub     = akakce.Publish;

        if (!telegram.ShareProductCardsOnIngest) return;
        if (string.IsNullOrWhiteSpace(telegram.BotToken)) return;

        var queue  = sp.GetRequiredService<AkakceTelegramPublishQueue>();
        var cache  = sp.GetRequiredService<IDistributedCache>();
        var logger = sp.GetRequiredService<ILogger<AkakceTelegramPublisherWorker>>();
        var sender = sp.GetRequiredService<IAkakceTelegramProductCardSender>();

        var istanbulHour = (DateTime.UtcNow.Hour + 3) % 24;
        var isQuiet      = istanbulHour >= pub.QuietStartHour && istanbulHour < pub.QuietEndHour;

        var queueSize = await queue.GetSizeAsync();
        if (isQuiet && queueSize < 5) return;

        var requiredDelay = ComputeRequiredDelaySeconds(queueSize, pub, isQuiet);
        var lastSendStr   = await cache.GetStringAsync(LastSendKey);
        if (!string.IsNullOrEmpty(lastSendStr) && long.TryParse(lastSendStr, out var lastUnix))
        {
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastUnix;
            if (elapsed < requiredDelay) return;
        }

        // Son gönderilen mağazayı oku, farklı mağazayı tercih et
        var lastMerchant = await cache.GetStringAsync(LastMerchantKey);

        var entry = await queue.DequeueTopAsync(avoidMerchant: lastMerchant);
        if (entry == null) return;

        if (isQuiet && entry.Score < 100)
        {
            await queue.EnqueueAsync(entry);
            return;
        }

        // Fingerprint kontrolü
        var fp       = $"{(int)entry.LowestPrice}_{(int)(entry.DiscountPercent ?? 0m)}";
        var storedFp = await cache.GetStringAsync(FingerprintPrefix + entry.ProductCode);
        if (!string.IsNullOrEmpty(storedFp) && storedFp == fp)
        {
            logger.LogDebug("Akakce publisher: fingerprint aynı, atlanıyor ({ProductCode})", entry.ProductCode);
            return;
        }

        logger.LogInformation(
            "Akakce publisher: gönderiliyor ({ProductCode}, trigger={Trigger}, score={Score:F0}, queueSize={QSize})",
            entry.ProductCode, entry.TriggerType, entry.Score, queueSize);

        try
        {
            await sender.TrySendAfterProductIngestedAsync(entry.ProductCode, entry.TriggerType);

            await cache.SetStringAsync(FingerprintPrefix + entry.ProductCode, fp,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) });

            await cache.SetStringAsync(LastSendKey,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });

            // Son gönderilen mağazayı kaydet (bir sonraki gönderimde farklı mağaza seçilsin)
            if (!string.IsNullOrEmpty(entry.MerchantName))
            {
                await cache.SetStringAsync(LastMerchantKey, entry.MerchantName,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) });
            }

            // Canlı izleme geçmişine kaydet
            try
            {
                var history  = sp.GetRequiredService<TelegramSentHistory>();
                var prodRepo = sp.GetRequiredService<IRepository<AkakceProduct, Guid>>();
                var prods    = await prodRepo.GetListAsync(p => p.ProductCode == entry.ProductCode);
                var prod     = prods.FirstOrDefault();
                await history.AddAsync(new TelegramSentItemDto
                {
                    Id              = entry.ProductCode,
                    Marketplace     = MarketplaceKind.Akakce,
                    Title           = prod?.Title ?? entry.ProductCode,
                    ImageUrl        = prod?.PrimaryImageUrl,
                    ProductUrl      = prod?.ProductUrl ?? "",
                    Price           = entry.LowestPrice,
                    PreviousPrice   = entry.PreviousPrice,
                    DiscountPercent = entry.DiscountPercent,
                    TriggerType     = entry.TriggerType,
                    SentAt          = DateTimeOffset.UtcNow,
                });
            }
            catch { /* best-effort */ }

            logger.LogInformation("Akakce publisher: gönderildi ✓ ({ProductCode})", entry.ProductCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Akakce publisher: gönderim başarısız ({ProductCode})", entry.ProductCode);
        }
    }

    private static int ComputeRequiredDelaySeconds(int queueSize, AkakcePublishOptions pub, bool isQuiet)
    {
        int minSec, maxSec;
        if (isQuiet)
        {
            minSec = 25 * 60; maxSec = 35 * 60;
        }
        else
        {
            var istHour     = (DateTime.UtcNow.Hour + 3) % 24;
            var isPrimeTime = istHour >= 18 || istHour < 1; // 18:00-01:00 İstanbul

            if (isPrimeTime)
            {
                if (queueSize >= 20)       { minSec = pub.MinDelayMinutes * 60; maxSec = (pub.MinDelayMinutes + 2) * 60; }
                else if (queueSize >= 5)   { minSec = pub.MinDelayMinutes * 60; maxSec = (int)((pub.MinDelayMinutes + pub.MaxDelayMinutes) * 0.5 * 60); }
                else                       { minSec = pub.MinDelayMinutes * 60; maxSec = pub.MaxDelayMinutes * 60; }
            }
            else
            {
                if (queueSize >= 20)       { minSec = pub.MinDelayMinutes * 60; maxSec = (pub.MinDelayMinutes + 3) * 60; }
                else if (queueSize >= 10)  { minSec = pub.MinDelayMinutes * 60; maxSec = (int)((pub.MinDelayMinutes + pub.MaxDelayMinutes) * 0.5 * 60); }
                else                       { minSec = (int)((pub.MinDelayMinutes + pub.MaxDelayMinutes) * 0.5 * 60); maxSec = pub.MaxDelayMinutes * 60; }
            }
        }
        return Rng.Next(minSec, maxSec + 1);
    }
}
