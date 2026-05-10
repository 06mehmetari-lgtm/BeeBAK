using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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

    public async Task TrySendAfterProductIngestedAsync(string contentId, CancellationToken cancellationToken = default)
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

        var dedupHours = Math.Clamp(opts.TelegramCardDedupHours, 0, 168);
        if (dedupHours > 0 && _distributedCache != null)
        {
            var dedupKey = $"beebak:tg-share-card:{contentId.Trim()}";
            var existing = await _distributedCache.GetStringAsync(dedupKey, cancellationToken);
            if (!string.IsNullOrEmpty(existing))
            {
                return;
            }
        }

        var product = await _productRepository.FindByContentIdAsync(contentId, includeOffers: true, cancellationToken);
        if (product == null || product.Offers.Count == 0)
        {
            return;
        }

        if (!IsEligibleShareVisual(product))
        {
            return;
        }

        var merchantIds = product.Offers.Select(o => o.MerchantId).Distinct().ToList();
        var mq = await _merchantRepository.GetQueryableAsync();
        var merchantsList = mq.Where(m => merchantIds.Contains(m.Id)).ToList();
        var merchantsById = merchantsList.ToDictionary(m => m.Id, m => m.Name);

        var bestUrl = PickBestMerchantUrl(product);
        if (string.IsNullOrWhiteSpace(bestUrl))
        {
            return;
        }

        var caption = BuildCaptionHtml(product, merchantsById);
        if (caption.Length > 1024)
        {
            caption = caption[..1021] + "…";
        }

        var token = opts.BotToken.Trim();
        var chatId = opts.ChatId.Trim();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        var photoUrl = product.PrimaryImageUrl?.Trim();
        var ok = false;
        if (!string.IsNullOrWhiteSpace(photoUrl) && Uri.TryCreate(photoUrl, UriKind.Absolute, out var pu) &&
            (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps))
        {
            ok = await TrySendPhotoAsync(client, token, chatId, photoUrl, caption, cancellationToken);
        }

        if (!ok)
        {
            ok = await TrySendMessageAsync(client, token, chatId, caption, cancellationToken);
        }

        if (ok && dedupHours > 0 && _distributedCache != null)
        {
            await _distributedCache.SetStringAsync(
                $"beebak:tg-share-card:{contentId.Trim()}",
                "1",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(dedupHours),
                },
                cancellationToken);
        }
    }

    private static bool IsEligibleShareVisual(CimriProduct product)
    {
        if (product.DiscountPercent is > 0m)
        {
            return HasClickableOffer(product);
        }

        if (product.PreviousPriceAmount is > 0m
            && product.BestPriceAmount is > 0m
            && product.PreviousPriceAmount > product.BestPriceAmount)
        {
            return HasClickableOffer(product);
        }

        return false;
    }

    private static bool HasClickableOffer(CimriProduct product)
    {
        return product.Offers.Any(o =>
            !string.IsNullOrWhiteSpace(o.MerchantProductUrl) || !string.IsNullOrWhiteSpace(o.OfferUrl));
    }

    private static string PickBestMerchantUrl(CimriProduct product)
    {
        var ordered = product.Offers
            .OrderBy(o => o.Price)
            .GroupBy(o => o.MerchantId)
            .Select(g => g.First())
            .FirstOrDefault();

        if (ordered == null)
        {
            return product.ProductUrl;
        }

        if (!string.IsNullOrWhiteSpace(ordered.MerchantProductUrl))
        {
            return ordered.MerchantProductUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(ordered.OfferUrl))
        {
            return ordered.OfferUrl.Trim();
        }

        return product.ProductUrl;
    }

    private string BuildCaptionHtml(CimriProduct product, Dictionary<Guid, string> merchantsById)
    {
        var offers = product.Offers.OrderBy(o => o.Price).ToList();
        var lowest = offers[0].Price;
        var currency = string.IsNullOrWhiteSpace(offers[0].Currency) ? "TRY" : offers[0].Currency.Trim();

        decimal? avg = null;
        if (offers.Count >= 2)
        {
            avg = offers.Average(o => o.Price);
        }

        decimal? bestPriceFrom = null;
        if (product.PreviousPriceAmount is > 0 && product.PreviousPriceAmount > lowest)
        {
            bestPriceFrom = product.PreviousPriceAmount.Value;
        }
        else if (product.DiscountPercent is > 0 and < 100)
        {
            var computed = lowest / (1m - product.DiscountPercent.Value / 100m);
            if (computed > lowest)
            {
                bestPriceFrom = computed;
            }
        }
        else if (offers.Count >= 2)
        {
            var maxPrice = offers.Max(o => o.Price);
            if (maxPrice > lowest)
            {
                bestPriceFrom = maxPrice;
            }
        }

        var merchantLabel = product.BestPriceMerchantName?.Trim();
        if (string.IsNullOrWhiteSpace(merchantLabel) && offers.Count > 0)
        {
            merchantsById.TryGetValue(offers[0].MerchantId, out var name);
            merchantLabel = name ?? offers[0].OfferTitle ?? offers[0].SellerName;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<b>Bee BAK Sana</b>");
        sb.Append("<b>").Append(EscapeHtml(product.Title.Trim())).AppendLine("</b>");
        sb.AppendLine();

        if (avg.HasValue)
        {
            sb.Append("Ortalama Fiyat: ").AppendLine(EscapeHtml(FormatMoney(avg.Value, currency)));
        }

        sb.Append("En Uygun Fiyat: ").AppendLine(EscapeHtml(FormatMoney(lowest, currency)));

        if (bestPriceFrom.HasValue && Math.Abs(bestPriceFrom.Value - lowest) >= 0.01m)
        {
            sb.Append(EscapeHtml(FormatMoney(bestPriceFrom.Value, currency)));
            sb.Append(" → ");
            sb.AppendLine(EscapeHtml(FormatMoney(lowest, currency)));
        }

        if (!string.IsNullOrWhiteSpace(merchantLabel))
        {
            sb.AppendLine(EscapeHtml(merchantLabel.Trim()));
        }

        var url = PickBestMerchantUrl(product);
        sb.AppendLine();
        sb.Append("<a href=\"").Append(EscapeAttr(url)).Append("\">En uygun teklife git →</a>");

        return sb.ToString();
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
