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

        // Tüm satıcıları yükle
        var merchantIds = offers.Select(o => o.MerchantId).Distinct().ToList();
        var merchantsList = await _merchantRepository.GetListAsync(
            m => merchantIds.Contains(m.Id), cancellationToken: cancellationToken);
        var merchantsById = merchantsList.ToDictionary(m => m.Id, m => m.Name);

        merchantsById.TryGetValue(cheapest.MerchantId, out var merchantName);
        merchantName ??= cheapest.SellerName?.Trim() ?? cheapest.OfferTitle?.Trim();

        // Ana CTA butonu için URL — Akakce redirect kullanma
        var bestUrl    = PickBestUrl(cheapest, product.ProductUrl);
        var buttonText = IsMerchantDirectUrl(bestUrl) ? "🛒 Ürüne Git →" : "🔗 Tüm teklifleri gör →";
        var replyMarkup = new
        {
            inline_keyboard = new[] { new[] { new { text = buttonText, url = bestUrl } } }
        };

        var caption = BuildCaptionHtml(product, offers, cheapest, merchantName, merchantsById, triggerType);
        if (caption.Length > 1024) caption = caption[..1021] + "…";

        var client = _httpClientFactory.CreateClient(CimriTelegramProductCardSender.HttpClientName);
        var token  = telegram.BotToken.Trim();
        var chatId = telegram.ChatId.Trim();

        var photoUrl = product.PrimaryImageUrl?.Trim();
        bool ok = false;
        if (!string.IsNullOrWhiteSpace(photoUrl) && Uri.TryCreate(photoUrl, UriKind.Absolute, out var pu)
            && (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps))
            ok = await TrySendPhotoAsync(client, token, chatId, photoUrl, caption, replyMarkup, cancellationToken);
        if (!ok)
            await TrySendMessageAsync(client, token, chatId, caption, replyMarkup, cancellationToken);
    }

    private static string BuildCaptionHtml(
        AkakceProduct product,
        System.Collections.Generic.List<AkakceOffer> offers,
        AkakceOffer cheapest,
        string? merchantName,
        System.Collections.Generic.Dictionary<Guid, string> merchantsById,
        string triggerType)
    {
        var lowest   = cheapest.Price;
        var currency = string.IsNullOrWhiteSpace(cheapest.Currency) ? "TRY" : cheapest.Currency.Trim();

        // Referans fiyat: en yüksek teklif (2+ varsa) veya önceki fiyat
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

        var sb = new StringBuilder();

        var header = triggerType switch
        {
            "price_drop"  => "💸 Fiyat düştü",
            "discount_up" => "📉 İndirim arttı",
            _ when discountPct >= 40 => "🔥 Fiyat ciddi seviyeye geriledi",
            _ when discountPct >= 20 => "🛒 Fiyat avantajı var",
            _             => "🐝 BeeBak Sana — Fırsat",
        };
        sb.AppendLine($"<b>{EscapeHtml(header)}</b>");
        sb.AppendLine();

        sb.AppendLine($"<b>{EscapeHtml(product.Title.Trim())}</b>");
        sb.AppendLine();

        // En ucuz teklif — öne çıkarılmış blok (link caption'da değil, inline button'da)
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine($"🥇 <b>{EscapeHtml(FormatMoney(lowest, currency))}</b>");
        if (!string.IsNullOrWhiteSpace(merchantName))
            sb.AppendLine($"   <b>{EscapeHtml(merchantName.Trim())}</b>");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();

        // Diğer tüm teklifler — varsa doğrudan mağaza linki
        var otherOffers = offers.Where(o => o.Price != lowest || o.MerchantId != cheapest.MerchantId).ToList();
        if (otherOffers.Count > 0)
        {
            sb.AppendLine("📋 <b>Diğer Teklifler:</b>");
            foreach (var offer in otherOffers)
            {
                merchantsById.TryGetValue(offer.MerchantId, out var offerSeller);
                offerSeller = offerSeller?.Trim()
                           ?? offer.SellerName?.Trim()
                           ?? offer.OfferTitle?.Trim()
                           ?? "Satıcı";

                var priceStr = EscapeHtml(FormatMoney(offer.Price, currency));
                var sellerStr = EscapeHtml(offerSeller);
                var directUrl = offer.MerchantProductUrl?.Trim();

                if (!string.IsNullOrWhiteSpace(directUrl))
                    sb.AppendLine($"• <a href=\"{EscapeAttr(directUrl)}\">{priceStr}</a> — {sellerStr}");
                else
                    sb.AppendLine($"• {priceStr} — {sellerStr}");
            }
            sb.AppendLine();
        }

        if (discountPct is > 0)
        {
            sb.AppendLine($"📉 En ucuz teklif yaklaşık %{(int)discountPct.Value} daha avantajlı");
            sb.AppendLine();
        }

        sb.Append("<i>⏳ Stok ve fiyat kısa sürede değişebilir.</i>");

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
        string photoUrl, string caption, object replyMarkup, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{botToken}/sendPhoto",
                new { chat_id = chatId, photo = photoUrl, caption, parse_mode = "HTML", reply_markup = replyMarkup }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Akakce Telegram sendPhoto failed"); return false; }
    }

    private async Task<bool> TrySendMessageAsync(
        HttpClient client, string botToken, string chatId,
        string text, object replyMarkup, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{botToken}/sendMessage",
                new { chat_id = chatId, text, parse_mode = "HTML", disable_web_page_preview = false, reply_markup = replyMarkup }, ct);
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
        try { return amount.ToString("N2", Tr) + " " + currency.ToUpperInvariant(); }
        catch { return $"{amount:0.##} {currency}"; }
    }
}
