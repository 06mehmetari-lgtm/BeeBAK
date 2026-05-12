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

        // En ucuz teklif — sadece URL'si olan teklifler arasından, tüm teklifleri fiyata göre sırala
        var offers = product.Offers.OrderBy(o => o.Price).ToList();
        var cheapest = offers.FirstOrDefault(o =>
            !string.IsNullOrWhiteSpace(o.SiteRedirectUrl)
            || !string.IsNullOrWhiteSpace(o.MerchantProductUrl)
            || !string.IsNullOrWhiteSpace(o.OfferUrl))
            ?? offers[0];

        var merchant = await _merchantRepository.FindAsync(cheapest.MerchantId, cancellationToken: cancellationToken);
        var merchantName = merchant?.Name?.Trim()
                        ?? cheapest.SellerName?.Trim()
                        ?? cheapest.OfferTitle?.Trim();

        var caption = BuildCaptionHtml(product, offers, cheapest, merchantName, triggerType);
        if (caption.Length > 1024) caption = caption[..1021] + "…";

        var client = _httpClientFactory.CreateClient(CimriTelegramProductCardSender.HttpClientName);
        var token  = telegram.BotToken.Trim();
        var chatId = telegram.ChatId.Trim();

        var ok = await TrySendRenderedCardAsync(client, token, chatId, product, offers, cheapest, merchantName, caption, cancellationToken);
        if (!ok)
        {
            var photoUrl = product.PrimaryImageUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(photoUrl) && Uri.TryCreate(photoUrl, UriKind.Absolute, out var pu)
                && (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps))
                ok = await TrySendPhotoAsync(client, token, chatId, photoUrl, caption, cancellationToken);
        }
        if (!ok)
            await TrySendMessageAsync(client, token, chatId, caption, cancellationToken);
    }

    private static string BuildCaptionHtml(
        AkakceProduct product,
        System.Collections.Generic.List<AkakceOffer> offers,
        AkakceOffer cheapest,
        string? merchantName,
        string triggerType)
    {
        var lowest   = cheapest.Price;
        var currency = string.IsNullOrWhiteSpace(cheapest.Currency) ? "TRY" : cheapest.Currency.Trim();

        // Piyasa Fiyatı = en yüksek teklif (2+ teklif varsa), yoksa PreviousPriceAmount
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

        if (marketPrice.HasValue)
        {
            sb.AppendLine("Türkiye piyasa ortalaması:");
            sb.AppendLine($"<s>{EscapeHtml(FormatMoney(marketPrice.Value, currency))}</s>");
            sb.AppendLine();
        }

        sb.AppendLine("💸 Anlık fiyat:");
        sb.AppendLine($"<b>{EscapeHtml(FormatMoney(lowest, currency))}</b>");

        if (discountPct is > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"📉 Yaklaşık %{(int)discountPct.Value} daha uygun");
        }

        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(merchantName))
        {
            sb.AppendLine("🛒 Satıcı:");
            sb.AppendLine($"{EscapeHtml(merchantName.Trim())}");
            sb.AppendLine();
        }

        var url = PickBestUrl(cheapest, product.ProductUrl);
        if (IsMerchantDirectUrl(url))
            sb.Append($"🔗 <a href=\"{EscapeAttr(url)}\">Ürüne Git</a>");
        else
            sb.Append($"🔗 <a href=\"{EscapeAttr(url)}\">Tüm teklifleri gör</a>");

        sb.AppendLine();
        sb.AppendLine();
        sb.Append("<i>⏳ Stok ve fiyat kısa sürede değişebilir.</i>");

        return sb.ToString().TrimEnd();
    }

    private static string PickBestUrl(AkakceOffer offer, string productUrl)
    {
        // Sadece mağazanın kendi doğrudan URL'si — Akakçe redirect'i asla kullanma
        if (!string.IsNullOrWhiteSpace(offer.MerchantProductUrl)) return offer.MerchantProductUrl.Trim();
        return productUrl; // Akakçe ürün sayfası (kullanıcı tüm teklifleri görür)
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

    private async Task<bool> TrySendPhotoAsync(HttpClient client, string botToken, string chatId, string photoUrl, string caption, CancellationToken ct)
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

    private async Task<bool> TrySendMessageAsync(HttpClient client, string botToken, string chatId, string text, CancellationToken ct)
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
        try { return amount.ToString("N2", Tr) + " " + currency.ToUpperInvariant(); }
        catch { return $"{amount:0.##} {currency}"; }
    }
}
