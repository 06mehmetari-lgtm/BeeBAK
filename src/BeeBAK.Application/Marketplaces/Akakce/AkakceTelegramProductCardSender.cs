using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Marketplaces.Cimri;
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
    private readonly ILogger<AkakceTelegramProductCardSender> _logger;
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public AkakceTelegramProductCardSender(
        IAkakceProductRepository productRepository,
        IRepository<AkakceMerchant, Guid> merchantRepository,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CimriClientOptions> cimriOptions,
        ILogger<AkakceTelegramProductCardSender> logger)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _httpClientFactory = httpClientFactory;
        _cimriOptions = cimriOptions;
        _logger = logger;
    }

    public async Task TrySendAfterProductIngestedAsync(
        string productCode,
        string triggerType = "new",
        CancellationToken cancellationToken = default)
    {
        var telegram = _cimriOptions.CurrentValue.Telegram;
        if (!telegram.ShareProductCardsOnIngest
            || string.IsNullOrWhiteSpace(telegram.BotToken)
            || string.IsNullOrWhiteSpace(telegram.ChatId))
            return;

        var product = await _productRepository.FindByProductCodeAsync(productCode, includeOffers: true, cancellationToken);
        if (product == null || product.Offers.Count == 0) return;

        var offers = product.Offers.OrderBy(o => o.Price).ToList();
        var cheapest = offers.FirstOrDefault(o =>
            !string.IsNullOrWhiteSpace(o.SiteRedirectUrl)
            || !string.IsNullOrWhiteSpace(o.MerchantProductUrl)
            || !string.IsNullOrWhiteSpace(o.OfferUrl))
            ?? offers[0];

        var merchantIds = offers.Select(o => o.MerchantId).Distinct().ToList();
        var merchantsList = await _merchantRepository.GetListAsync(
            m => merchantIds.Contains(m.Id), cancellationToken: cancellationToken);
        var merchantsById = merchantsList.ToDictionary(m => m.Id, m => m.Name);

        merchantsById.TryGetValue(cheapest.MerchantId, out var merchantName);
        merchantName ??= cheapest.SellerName?.Trim() ?? cheapest.OfferTitle?.Trim();

        var bestUrl    = PickBestUrl(cheapest, product.ProductUrl);

        var caption = BuildCaptionHtml(product, offers, cheapest, merchantName, merchantsById, bestUrl, triggerType);
        if (caption.Length > 1024) caption = caption[..1021] + "…";

        var client = _httpClientFactory.CreateClient(CimriTelegramProductCardSender.HttpClientName);
        var token  = telegram.BotToken.Trim();
        var chatId = telegram.ChatId.Trim();

        var photoUrl = product.PrimaryImageUrl?.Trim();
        bool ok = false;
        if (!string.IsNullOrWhiteSpace(photoUrl) && Uri.TryCreate(photoUrl, UriKind.Absolute, out var pu)
            && (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps))
            ok = await TrySendPhotoAsync(client, token, chatId, photoUrl, caption, cancellationToken);
        if (!ok)
            await TrySendMessageAsync(client, token, chatId, caption, cancellationToken);
    }

    private static string BuildCaptionHtml(
        AkakceProduct product,
        System.Collections.Generic.List<AkakceOffer> offers,
        AkakceOffer cheapest,
        string? merchantName,
        System.Collections.Generic.Dictionary<Guid, string> merchantsById,
        string bestUrl,
        string triggerType)
    {
        var lowest   = cheapest.Price;
        var currency = string.IsNullOrWhiteSpace(cheapest.Currency) ? "TRY" : cheapest.Currency.Trim();

        decimal? discountPct = null;
        decimal? avgPrice = null;
        if (offers.Count > 1)
        {
            avgPrice = offers.Average(o => o.Price);
            if (avgPrice > lowest)
                discountPct = Math.Round((avgPrice.Value - lowest) / avgPrice.Value * 100m);
        }
        else if (product.PreviousPriceAmount is > 0 && product.PreviousPriceAmount > lowest)
        {
            avgPrice = product.PreviousPriceAmount.Value;
            discountPct = Math.Round((product.PreviousPriceAmount.Value - lowest) / product.PreviousPriceAmount.Value * 100m);
        }

        var sb = new StringBuilder();

        // ── Başlık ───────────────────────────────────────────────────────────
        var header = triggerType switch
        {
            "price_drop"  => "🚨 FİYAT DÜŞTÜ",
            "discount_up" => "📉 İNDİRİM ARTTI",
            _ when discountPct >= 50 => "🔥 BÜYÜK FIRSAT YAKALANDI",
            _ when discountPct >= 20 => "🚨 FIRSAT YAKALANDI",
            _             => "🚨 FIRSAT YAKALANDI",
        };
        sb.AppendLine($"<b>{EscapeHtml(header)}</b>");
        sb.AppendLine();

        // ── Ürün başlığı ─────────────────────────────────────────────────────
        sb.AppendLine($"<b>{EscapeHtml(product.Title.Trim())}</b>");
        sb.AppendLine();

        // ── En ucuz teklif kutusu ────────────────────────────────────────────
        sb.AppendLine("╭──────────────");
        sb.AppendLine("💰 <b>EN DÜŞÜK FİYAT</b>");
        sb.AppendLine($"<b>{EscapeHtml(FormatMoney(lowest, currency))}</b>");
        var linkLabel = IsMerchantDirectUrl(bestUrl) ? "🛒 Ürünü İncele →" : "🛒 Teklife Git →";
        sb.AppendLine($"<a href=\"{EscapeAttr(bestUrl)}\">{linkLabel}</a>");
        if (!string.IsNullOrWhiteSpace(merchantName))
            sb.AppendLine($"🏪 {EscapeHtml(merchantName.Trim())}");
        if (avgPrice.HasValue && avgPrice.Value > lowest)
            sb.AppendLine($"💸 ≈ {EscapeHtml(FormatMoney(avgPrice.Value - lowest, currency))} tasarruf");
        if (discountPct is > 0)
        {
            var score = Math.Min(Math.Round(discountPct.Value / 10m, 1), 10m);
            sb.AppendLine($"🧠 Fırsat Skoru: {score.ToString("0.#", Tr)} / 10");
        }
        if (cheapest.ScrapedUtc != default)
        {
            var ago = DateTime.UtcNow - cheapest.ScrapedUtc;
            var agoStr = ago.TotalMinutes < 60
                ? $"{(int)ago.TotalMinutes} dk önce"
                : ago.TotalHours < 24
                    ? $"{(int)ago.TotalHours} saat önce"
                    : $"{(int)ago.TotalDays} gün önce";
            sb.AppendLine($"⏳ {agoStr} güncellendi");
        }
        sb.AppendLine("╰──────────────");
        sb.AppendLine();

        sb.AppendLine("⚠️ <i>Fiyatlar hızlı değişebilir</i>");
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━");
        sb.AppendLine();

        // ── Diğer satıcılar ──────────────────────────────────────────────────
        var otherOffers = offers.Where(o => o.Price != lowest || o.MerchantId != cheapest.MerchantId).ToList();
        if (otherOffers.Count > 0)
        {
            sb.AppendLine("📊 <b>Diğer Satıcılar</b>");
            foreach (var offer in otherOffers)
            {
                merchantsById.TryGetValue(offer.MerchantId, out var offerSeller);
                offerSeller = offerSeller?.Trim()
                           ?? offer.SellerName?.Trim()
                           ?? offer.OfferTitle?.Trim()
                           ?? "Satıcı";

                var priceStr  = EscapeHtml(FormatMoney(offer.Price, currency));
                var sellerStr = EscapeHtml(offerSeller);
                var directUrl = offer.MerchantProductUrl?.Trim();

                if (!string.IsNullOrWhiteSpace(directUrl))
                    sb.AppendLine($"• <a href=\"{EscapeAttr(directUrl)}\">{priceStr} — {sellerStr}</a>");
                else
                    sb.AppendLine($"• {priceStr} — {sellerStr}");
            }
            sb.AppendLine();
        }

        // ── Ortalama karşılaştırması ─────────────────────────────────────────
        if (discountPct is > 0)
            sb.Append($"📉 Piyasa ortalamasından %{(int)discountPct.Value} daha avantajlı");

        return sb.ToString().TrimEnd();
    }

    private static string PickBestUrl(AkakceOffer offer, string productUrl)
    {
        if (!string.IsNullOrWhiteSpace(offer.MerchantProductUrl)) return offer.MerchantProductUrl.Trim();
        return productUrl;
    }

    private static bool IsMerchantDirectUrl(string url) =>
        !url.Contains("akakce.com", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> TrySendRenderedCardAsync(
        HttpClient client,
        string botToken, string chatId,
        AkakceProduct product,
        System.Collections.Generic.List<AkakceOffer> offers,
        AkakceOffer cheapest,
        string? merchantName,
        string caption,
        CancellationToken cancellationToken)
    {
        try
        {
            var lowest   = cheapest.Price;
            var currency = string.IsNullOrWhiteSpace(cheapest.Currency) ? "TRY" : cheapest.Currency.Trim();

            decimal? marketPrice = null;
            if (offers.Count >= 2)
            {
                var maxOffer = offers.Max(o => o.Price);
                if (maxOffer > lowest) marketPrice = maxOffer;
            }
            if (!marketPrice.HasValue && product.PreviousPriceAmount is > 0 && product.PreviousPriceAmount > lowest)
                marketPrice = product.PreviousPriceAmount.Value;

            decimal? discountPct = null;
            if (marketPrice.HasValue && marketPrice.Value > 0)
                discountPct = Math.Round((marketPrice.Value - lowest) / marketPrice.Value * 100m);

            var themeIndex = Math.Abs(product.ProductCode.GetHashCode()) % 4;

            var cardBytes = await CimriCardImageGenerator.GenerateAsync(
                product.Title?.Trim() ?? "",
                product.PrimaryImageUrl?.Trim(),
                lowest, null, marketPrice, currency,
                merchantName?.Trim(),
                discountPct,
                themeIndex,
                client,
                cancellationToken);

            var tgUrl = $"https://api.telegram.org/bot{botToken}/sendPhoto";
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId), "chat_id");
            form.Add(new StringContent(caption), "caption");
            form.Add(new StringContent("HTML"), "parse_mode");
            var imgContent = new ByteArrayContent(cardBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(imgContent, "photo", $"card_{product.ProductCode}.png");

            using var response = await client.PostAsync(tgUrl, form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("Akakce Telegram sendPhoto (card) HTTP {Status}: {Body}", response.StatusCode, body);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Akakce kart render/upload başarısız, fallback'e geçiliyor");
            return false;
        }
    }

    private async Task<bool> TrySendPhotoAsync(
        HttpClient client, string botToken, string chatId,
        string photoUrl, string caption, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{botToken}/sendPhoto",
                new { chat_id = chatId, photo = photoUrl, caption, parse_mode = "HTML" }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Akakce Telegram sendPhoto failed"); return false; }
    }

    private async Task<bool> TrySendMessageAsync(
        HttpClient client, string botToken, string chatId,
        string text, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{botToken}/sendMessage",
                new { chat_id = chatId, text, parse_mode = "HTML", disable_web_page_preview = false }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Akakce Telegram sendMessage failed"); return false; }
    }

    private static string EscapeHtml(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EscapeAttr(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string FormatMoney(decimal amount, string currency)
    {
        var display = currency.Equals("TRY", StringComparison.OrdinalIgnoreCase) ? "TL" : currency.ToUpperInvariant();
        try { return amount.ToString("N2", Tr) + " " + display; }
        catch { return $"{amount:0.##} {display}"; }
    }
}
