using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Marketplaces.Cimri;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceTelegramProductCardSender : IAkakceTelegramProductCardSender, ITransientDependency
{
    private readonly IAkakceProductRepository _productRepository;
    private readonly IRepository<AkakceMerchant, Guid> _merchantRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CimriClientOptions> _cimriOptions;
    private readonly IDistributedCache? _distributedCache;
    private readonly ILogger<AkakceTelegramProductCardSender> _logger;
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public AkakceTelegramProductCardSender(
        IAkakceProductRepository productRepository,
        IRepository<AkakceMerchant, Guid> merchantRepository,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CimriClientOptions> cimriOptions,
        ILogger<AkakceTelegramProductCardSender> logger,
        IDistributedCache? distributedCache = null)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _httpClientFactory = httpClientFactory;
        _cimriOptions = cimriOptions;
        _logger = logger;
        _distributedCache = distributedCache;
    }

    public async Task TrySendAfterProductIngestedAsync(string productCode, CancellationToken cancellationToken = default)
    {
        var telegram = _cimriOptions.CurrentValue.Telegram;
        if (!telegram.ShareProductCardsOnIngest
            || string.IsNullOrWhiteSpace(telegram.BotToken)
            || string.IsNullOrWhiteSpace(telegram.ChatId))
        {
            return;
        }

        var product = await _productRepository.FindByProductCodeAsync(productCode, includeOffers: true, cancellationToken);
        if (product == null
            || product.Offers.Count == 0
            || product.DiscountPercent < telegram.MinDiscountPercentForTelegram)
        {
            return;
        }

        var dedupHours = Math.Clamp(telegram.TelegramCardDedupHours, 0, 168);
        var dedupKey = $"beebak:tg-share-card:akakce:{productCode.Trim()}";
        if (dedupHours > 0 && _distributedCache != null)
        {
            var existing = await _distributedCache.GetStringAsync(dedupKey, cancellationToken);
            if (!string.IsNullOrEmpty(existing))
            {
                return;
            }
        }

        var best = product.Offers
            .Where(o => !string.IsNullOrWhiteSpace(o.SiteRedirectUrl)
                        || !string.IsNullOrWhiteSpace(o.MerchantProductUrl)
                        || !string.IsNullOrWhiteSpace(o.OfferUrl))
            .OrderBy(o => o.Price)
            .FirstOrDefault();
        if (best == null)
        {
            return;
        }

        var merchant = await _merchantRepository.FindAsync(best.MerchantId, cancellationToken: cancellationToken);
        var caption = BuildCaptionHtml(product, best, merchant?.Name);
        if (caption.Length > 1024)
        {
            caption = caption[..1021] + "...";
        }

        var client = _httpClientFactory.CreateClient(CimriTelegramProductCardSender.HttpClientName);
        var ok = false;
        var photoUrl = product.PrimaryImageUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(photoUrl)
            && Uri.TryCreate(photoUrl, UriKind.Absolute, out var pu)
            && (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps))
        {
            ok = await TrySendPhotoAsync(client, telegram.BotToken.Trim(), telegram.ChatId.Trim(), photoUrl, caption, cancellationToken);
        }

        if (!ok)
        {
            ok = await TrySendMessageAsync(client, telegram.BotToken.Trim(), telegram.ChatId.Trim(), caption, cancellationToken);
        }

        if (ok && dedupHours > 0 && _distributedCache != null)
        {
            await _distributedCache.SetStringAsync(
                dedupKey,
                "1",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(dedupHours) },
                cancellationToken);
        }
    }

    private static string BuildCaptionHtml(AkakceProduct product, AkakceOffer best, string? merchantName)
    {
        var target = best.SiteRedirectUrl ?? best.MerchantProductUrl ?? best.OfferUrl ?? product.ProductUrl;
        var previous = product.PreviousPriceAmount;
        if (previous is null && product.DiscountPercent is > 0 and < 100)
        {
            previous = best.Price / (1m - product.DiscountPercent.Value / 100m);
        }

        var sb = new StringBuilder();
        sb.AppendLine("<b>Bee BAK Sana</b>");
        sb.Append("<b>").Append(EscapeHtml(product.Title)).AppendLine("</b>");
        sb.AppendLine();
        sb.Append("En Uygun Fiyat: ").AppendLine(EscapeHtml(FormatMoney(best.Price, best.Currency)));
        if (previous is > 0 && previous > best.Price)
        {
            sb.Append(EscapeHtml(FormatMoney(previous.Value, best.Currency))).Append(" -> ")
                .AppendLine(EscapeHtml(FormatMoney(best.Price, best.Currency)));
        }

        if (product.DiscountPercent is > 0)
        {
            sb.AppendLine($"Indirim: %{product.DiscountPercent:0}");
        }

        if (!string.IsNullOrWhiteSpace(merchantName))
        {
            sb.AppendLine(EscapeHtml(merchantName));
        }

        sb.AppendLine();
        sb.Append("<a href=\"").Append(EscapeAttr(target)).Append("\">En uygun teklife git -></a>");
        return sb.ToString();
    }

    private static string EscapeHtml(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EscapeAttr(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string FormatMoney(decimal amount, string currency) =>
        amount.ToString("N2", Tr) + " " + (string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.ToUpperInvariant());

    private async Task<bool> TrySendPhotoAsync(HttpClient client, string botToken, string chatId, string photoUrl, string caption, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{botToken}/sendPhoto",
                new { chat_id = chatId, photo = photoUrl, caption, parse_mode = "HTML" },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Akakce Telegram sendPhoto failed");
            return false;
        }
    }

    private async Task<bool> TrySendMessageAsync(HttpClient client, string botToken, string chatId, string text, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{botToken}/sendMessage",
                new { chat_id = chatId, text, parse_mode = "HTML", disable_web_page_preview = false },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Akakce Telegram sendMessage failed");
            return false;
        }
    }
}
