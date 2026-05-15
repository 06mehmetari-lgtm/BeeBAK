using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriTelegramProductCardSender : ICimriTelegramProductCardSender, ITransientDependency
{
    public const string HttpClientName = CimriTelegramListingSyncNotifier.HttpClientName;

    private readonly ICimriProductRepository _productRepository;
    private readonly IRepository<CimriMerchant, Guid> _merchantRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly IDistributedCache? _distributedCache;
    private readonly ILogger<CimriTelegramProductCardSender> _logger;

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public CimriTelegramProductCardSender(
        ICimriProductRepository productRepository,
        IRepository<CimriMerchant, Guid> merchantRepository,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CimriClientOptions> options,
        ILogger<CimriTelegramProductCardSender> logger,
        IDistributedCache? distributedCache = null)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _distributedCache = distributedCache;
    }

    public async Task TrySendAfterProductIngestedAsync(
        string contentId,
        string triggerType = "new",
        CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue.Telegram;
        if (!opts.ShareProductCardsOnIngest)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(opts.BotToken) || string.IsNullOrWhiteSpace(opts.ChatId))
        {
            return;
        }

        var product = await _productRepository.FindByContentIdAsync(contentId, includeOffers: true, cancellationToken);
        if (product == null || product.Offers.Count == 0)
        {
            return;
        }

        if (!IsEligibleShareVisual(product, opts.MinDiscountPercentForTelegram))
        {
            return;
        }

        var merchantIds = product.Offers.Select(o => o.MerchantId).Distinct().ToList();
        var merchantsList = await _merchantRepository.GetListAsync(
            m => merchantIds.Contains(m.Id), cancellationToken: cancellationToken);
        var merchantsById = merchantsList.ToDictionary(m => m.Id, m => m.Name);

        var bestUrl = PickBestMerchantUrl(product);
        if (string.IsNullOrWhiteSpace(bestUrl))
        {
            return;
        }

        var caption = BuildCaptionHtml(product, merchantsById, triggerType);
        if (caption.Length > 1024)
        {
            caption = caption[..1021] + "…";
        }

        var token = opts.BotToken.Trim();
        var chatId = opts.ChatId.Trim();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        // Orijinal ürün fotoğrafını gönder (overlay/gölge içermeyen)
        var photoUrl = product.PrimaryImageUrl?.Trim();
        bool ok = false;
        if (!string.IsNullOrWhiteSpace(photoUrl) && Uri.TryCreate(photoUrl, UriKind.Absolute, out var pu) &&
            (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps))
        {
            ok = await TrySendPhotoAsync(client, token, chatId, photoUrl, caption, cancellationToken);
        }

        if (!ok)
        {
            ok = await TrySendMessageAsync(client, token, chatId, caption, cancellationToken);
        }

        _ = ok; // sonuç publisher worker tarafından loglanır
    }

    private static bool IsEligibleShareVisual(CimriProduct product, decimal minDiscountPercent)
    {
        if (!HasClickableOffer(product)) return false;

        var offers = product.Offers.OrderBy(o => o.Price).ToList();
        if (offers.Count == 0) return false;

        var lowest = offers[0].Price;

        // Gerçek piyasa avantajı hesapla (sahte indirim filtresi)
        decimal? realDiscount = null;
        if (offers.Count >= 2)
        {
            var maxOffer = offers.Max(o => o.Price);
            if (maxOffer > lowest)
                realDiscount = (maxOffer - lowest) / maxOffer * 100m;
        }
        if (!realDiscount.HasValue && product.PreviousPriceAmount is > 0m && product.PreviousPriceAmount > lowest)
            realDiscount = (product.PreviousPriceAmount.Value - lowest) / product.PreviousPriceAmount.Value * 100m;

        // Gerçek indirim varsa eşik kontrolü; yoksa Cimri'nin kendi değerine bak
        if (realDiscount.HasValue)
            return realDiscount.Value >= Math.Max(minDiscountPercent, 5m);

        // Gerçek piyasa verisi yoksa Cimri'nin indirim değerine bak
        if (product.DiscountPercent >= minDiscountPercent)
            return true;

        return false;
    }

    private static bool HasClickableOffer(CimriProduct product)
    {
        return product.Offers.Any(o =>
            !string.IsNullOrWhiteSpace(o.MerchantProductUrl) || !string.IsNullOrWhiteSpace(o.OfferUrl));
    }

    private static string PickBestMerchantUrl(CimriProduct product)
    {
        var cheapest = product.Offers.OrderBy(o => o.Price).FirstOrDefault();
        if (cheapest == null) return product.ProductUrl;

        // Sadece mağazanın kendi doğrudan URL'si — Cimri redirect'i asla kullanma
        if (!string.IsNullOrWhiteSpace(cheapest.MerchantProductUrl))
            return cheapest.MerchantProductUrl.Trim();

        // Doğrudan URL yoksa Cimri ürün sayfası (kullanıcı tüm teklifleri görür)
        return product.ProductUrl;
    }

    private static bool IsMerchantDirectUrl(string url) =>
        !url.Contains("cimri.com", StringComparison.OrdinalIgnoreCase);

    private string BuildCaptionHtml(CimriProduct product, Dictionary<Guid, string> merchantsById, string triggerType = "new")
    {
        var offers = product.Offers.OrderBy(o => o.Price).ToList();
        var lowest = offers[0].Price;
        var currency = string.IsNullOrWhiteSpace(offers[0].Currency) ? "TRY" : offers[0].Currency.Trim();

        // Referans fiyat: en yüksek teklif (2+ varsa) veya önceki fiyat
        decimal? marketPrice = null;
        if (offers.Count >= 2)
        {
            var maxOffer = offers.Max(o => o.Price);
            if (maxOffer > lowest)
                marketPrice = maxOffer;
        }
        if (!marketPrice.HasValue && product.PreviousPriceAmount is > 0 && product.PreviousPriceAmount > lowest)
            marketPrice = product.PreviousPriceAmount.Value;

        decimal? discountPct = null;
        if (marketPrice.HasValue && marketPrice.Value > 0)
            discountPct = Math.Round((marketPrice.Value - lowest) / marketPrice.Value * 100m);

        var url = PickBestMerchantUrl(product);

        var sb = new StringBuilder();

        // ── Başlık ────────────────────────────────────────────────────────────
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

        // ── Ürün adı ──────────────────────────────────────────────────────────
        sb.AppendLine($"<b>{EscapeHtml(product.Title.Trim())}</b>");
        sb.AppendLine();

        // ── Tüm platform fiyatları (max 5 teklif) ────────────────────────────
        var displayOffers = offers.Take(5).ToList();
        sb.AppendLine("📊 <b>Platform Fiyatları:</b>");
        foreach (var offer in displayOffers)
        {
            merchantsById.TryGetValue(offer.MerchantId, out var offerSeller);
            offerSeller = offerSeller?.Trim()
                       ?? offer.SellerName?.Trim()
                       ?? offer.OfferTitle?.Trim()
                       ?? product.BestPriceMerchantName?.Trim()
                       ?? "Satıcı";
            var star = offer.Price == lowest ? " 🏆" : "";
            sb.AppendLine($"• <b>{EscapeHtml(FormatMoney(offer.Price, currency))}</b> — {EscapeHtml(offerSeller)}{star}");
        }
        if (offers.Count > 5)
            sb.AppendLine($"  <i>+{offers.Count - 5} teklif daha…</i>");
        sb.AppendLine();

        // ── İndirim özeti ─────────────────────────────────────────────────────
        if (discountPct is > 0)
        {
            sb.AppendLine($"📉 En ucuz teklif yaklaşık %{(int)discountPct.Value} daha avantajlı");
            sb.AppendLine();
        }

        // ── CTA ───────────────────────────────────────────────────────────────
        if (IsMerchantDirectUrl(url))
            sb.Append($"🔗 <a href=\"{EscapeAttr(url)}\">Ürüne Git</a>");
        else
            sb.Append($"🔗 <a href=\"{EscapeAttr(url)}\">Tüm teklifleri gör</a>");

        sb.AppendLine();
        sb.AppendLine();
        sb.Append("<i>⏳ Stok ve fiyat kısa sürede değişebilir.</i>");

        return sb.ToString().TrimEnd();
    }

    private static string EscapeHtml(string s)
    {
        return s
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string EscapeAttr(string s)
    {
        return s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string FormatMoney(decimal amount, string currency)
    {
        try
        {
            return amount.ToString("N2", Tr) + " " + currency.ToUpperInvariant();
        }
        catch
        {
            return $"{amount:0.##} {currency}";
        }
    }

    private async Task<bool> TrySendRenderedCardAsync(
        HttpClient client,
        string botToken,
        string chatId,
        CimriProduct product,
        Dictionary<Guid, string> merchantsById,
        string caption,
        CancellationToken cancellationToken)
    {
        try
        {
            var offers = product.Offers.OrderBy(o => o.Price).ToList();
            if (offers.Count == 0) return false;

            var lowest = offers[0].Price;
            var currency = string.IsNullOrWhiteSpace(offers[0].Currency) ? "TRY" : offers[0].Currency.Trim();

            // Piyasa Fiyatı = en yüksek teklif fiyatı (kart görseli için referans fiyat)
            decimal? marketPrice = null;
            if (offers.Count >= 2)
            {
                var maxOffer = offers.Max(o => o.Price);
                if (maxOffer > lowest) marketPrice = maxOffer;
            }
            if (!marketPrice.HasValue && product.PreviousPriceAmount is > 0 && product.PreviousPriceAmount > lowest)
                marketPrice = product.PreviousPriceAmount.Value;

            merchantsById.TryGetValue(offers[0].MerchantId, out var merchantName);
            merchantName ??= product.BestPriceMerchantName?.Trim()
                          ?? offers[0].OfferTitle
                          ?? offers[0].SellerName;

            decimal? discountPct = null;
            if (marketPrice.HasValue && marketPrice.Value > 0)
                discountPct = Math.Round((marketPrice.Value - lowest) / marketPrice.Value * 100m);

            // Temayı contentId hash'inden seç (her ürün tutarlı ama farklı renkte)
            var themeIndex = Math.Abs(product.ContentId.GetHashCode()) % 4;

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
            form.Add(imgContent, "photo", $"card_{product.ContentId}.png");

            using var response = await client.PostAsync(tgUrl, form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("Telegram sendPhoto (card) HTTP {Status}: {Body}", response.StatusCode, body);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kart render/upload başarısız, fallback'e geçiliyor");
            return false;
        }
    }

    private async Task<bool> TrySendPhotoAsync(
        HttpClient client,
        string botToken,
        string chatId,
        string photoUrl,
        string caption,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.telegram.org/bot{botToken}/sendPhoto";
        try
        {
            using var response = await client.PostAsJsonAsync(
                url,
                new
                {
                    chat_id = chatId,
                    photo = photoUrl,
                    caption,
                    parse_mode = "HTML",
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("Telegram sendPhoto HTTP {Status}: {Body}", response.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram sendPhoto failed");
            return false;
        }
    }

    private async Task<bool> TrySendMessageAsync(
        HttpClient client,
        string botToken,
        string chatId,
        string text,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
        try
        {
            using var response = await client.PostAsJsonAsync(
                url,
                new { chat_id = chatId, text, parse_mode = "HTML", disable_web_page_preview = false },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Telegram sendMessage HTTP {Status}: {Body}", response.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram sendMessage failed");
            return false;
        }
    }
}
