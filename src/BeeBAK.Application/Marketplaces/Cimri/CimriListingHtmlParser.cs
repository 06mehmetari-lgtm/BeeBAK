using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri liste sayfalarından (indirimli ürünler, kategori vb.) ürün kartlarını okur.
/// Her kart: title (h3), kategori slug ve contentId (anchor href), liste görseli, indirim oranı,
/// teklif sayısı, en iyi fiyat satıcısı, en iyi fiyat ve önceki fiyat alanlarını içerir.
/// </summary>
public static class CimriListingHtmlParser
{
    private static readonly Regex DiscountRegex = new(@"%\s*(\d+)", RegexOptions.Compiled);

    private static readonly Regex OfferCountRegex = new(@"(\d+)\s+fiyat", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ContentIdFromAnchorRegex = new(@",(\d+)(?:[/?#]|$)", RegexOptions.Compiled);

    public static IReadOnlyList<CimriListingCard> Parse(string html, string siteOriginUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<CimriListingCard>();
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var origin = TrimOrigin(siteOriginUrl);

        var articles = doc.DocumentNode.SelectNodes("//article[contains(@class,'wneDI')]")
                       ?? doc.DocumentNode.SelectNodes("//article[@data-size='listing']")
                       ?? doc.DocumentNode.SelectNodes("//article");

        if (articles == null)
        {
            return Array.Empty<CimriListingCard>();
        }

        var byContentId = new Dictionary<string, CimriListingCard>(StringComparer.Ordinal);

        foreach (var article in articles)
        {
            var card = ParseCard(article, origin);
            if (card == null)
            {
                continue;
            }

            byContentId[card.ContentId] = card;
        }

        return byContentId.Values.ToList();
    }

    private static CimriListingCard? ParseCard(HtmlNode article, string origin)
    {
        var anchor = article.SelectSingleNode(".//a[contains(@class,'SGuZK')]")
                  ?? article.SelectSingleNode(".//a[@href]");
        if (anchor == null)
        {
            return null;
        }

        var hrefRaw = anchor.GetAttributeValue("href", "")?.Trim();
        if (string.IsNullOrWhiteSpace(hrefRaw))
        {
            return null;
        }

        if (!TryToAbsoluteUrl(hrefRaw, origin, out var absoluteUrl))
        {
            return null;
        }

        if (!TryExtractContentId(absoluteUrl, out var contentId))
        {
            return null;
        }

        var slug = ExtractCategorySlugFromUrl(absoluteUrl);

        var titleNode = article.SelectSingleNode(".//h3");
        var title = HtmlEntity.DeEntitize(titleNode?.InnerText.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(title))
        {
            var imgAlt = article.SelectSingleNode(".//img[@alt]")?.GetAttributeValue("alt", "");
            title = HtmlEntity.DeEntitize(imgAlt ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var imageNode = article.SelectSingleNode(".//img[@srcset]") ?? article.SelectSingleNode(".//img[@src]");
        var imageUrl = ExtractBestImageUrl(imageNode);

        decimal? discountPercent = null;
        var discountSpan = article.SelectSingleNode(".//span[contains(@class,'DWR_r')]");
        if (discountSpan != null)
        {
            var match = DiscountRegex.Match(HtmlEntity.DeEntitize(discountSpan.InnerText));
            if (match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
            {
                discountPercent = pct;
            }
        }

        int? offerCount = null;
        var offerCountNode = article.SelectSingleNode(".//span[contains(@class,'koiag') and contains(@class,'LDIzJ')]")
                          ?? article.SelectSingleNode(".//span[contains(text(),'fiyat')]");
        if (offerCountNode != null)
        {
            var oc = OfferCountRegex.Match(HtmlEntity.DeEntitize(offerCountNode.InnerText));
            if (oc.Success && int.TryParse(oc.Groups[1].Value, out var n))
            {
                offerCount = n;
            }
        }

        string? bestMerchant = null;
        var merchantNode = article.SelectSingleNode(".//div[contains(@class,'IHhYD')]/span")
                        ?? article.SelectSingleNode(".//div[contains(@class,'IHhYD')]");
        if (merchantNode != null)
        {
            bestMerchant = HtmlEntity.DeEntitize(merchantNode.InnerText.Trim());
            if (string.IsNullOrWhiteSpace(bestMerchant))
            {
                bestMerchant = null;
            }
        }

        decimal? bestPrice = null;
        var bestPriceNode = article.SelectSingleNode(".//span[contains(@class,'h1Anp')]");
        if (bestPriceNode != null)
        {
            bestPrice = CimriPriceParser.TryParseTryAmount(bestPriceNode.InnerText);
        }

        decimal? previousPrice = null;
        var previousPriceNode = article.SelectSingleNode(".//span[contains(@class,'fvt5M')]")
                             ?? article.SelectSingleNode(".//span[contains(@class,'XFpik')]");
        if (previousPriceNode != null)
        {
            previousPrice = CimriPriceParser.TryParseTryAmount(previousPriceNode.InnerText);
        }

        return new CimriListingCard
        {
            ContentId = contentId,
            ProductUrl = absoluteUrl,
            CategorySlug = slug,
            Title = title,
            ImageUrl = imageUrl,
            DiscountPercent = discountPercent,
            OfferCount = offerCount,
            BestMerchantName = bestMerchant,
            BestPriceAmount = bestPrice,
            PreviousPriceAmount = previousPrice,
        };
    }

    private static string? ExtractBestImageUrl(HtmlNode? imageNode)
    {
        if (imageNode == null)
        {
            return null;
        }

        var srcset = imageNode.GetAttributeValue("srcset", null);
        if (!string.IsNullOrWhiteSpace(srcset))
        {
            var twoX = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(s => s.EndsWith("2x", StringComparison.OrdinalIgnoreCase));
            var pick = twoX ?? srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (!string.IsNullOrWhiteSpace(pick))
            {
                var spaceIdx = pick.IndexOf(' ', StringComparison.Ordinal);
                return spaceIdx > 0 ? pick[..spaceIdx] : pick;
            }
        }

        var src = imageNode.GetAttributeValue("src", null);
        return string.IsNullOrWhiteSpace(src) ? null : src;
    }

    public static bool TryExtractContentId(string url, out string contentId)
    {
        contentId = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var match = ContentIdFromAnchorRegex.Match(url);
        if (!match.Success)
        {
            return false;
        }

        contentId = match.Groups[1].Value;
        return true;
    }

    public static string? ExtractCategorySlugFromUrl(string absoluteUrl)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var first = segments[0].Trim();
        return string.IsNullOrEmpty(first) ? null : first;
    }

    public static bool TryToAbsoluteUrl(string href, string siteOriginUrl, out string absolute)
    {
        absolute = string.Empty;

        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var trimmed = href.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var abs)
            && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
        {
            absolute = abs.ToString();
            return true;
        }

        var origin = TrimOrigin(siteOriginUrl);
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        if (Uri.TryCreate(originUri, trimmed, out var combined))
        {
            absolute = combined.ToString();
            return true;
        }

        return false;
    }

    private static string TrimOrigin(string siteOriginUrl)
    {
        return string.IsNullOrWhiteSpace(siteOriginUrl) ? string.Empty : siteOriginUrl.TrimEnd('/');
    }
}
