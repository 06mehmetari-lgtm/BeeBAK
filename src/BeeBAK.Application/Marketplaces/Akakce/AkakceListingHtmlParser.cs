using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace BeeBAK.Marketplaces.Akakce;

public static class AkakceListingHtmlParser
{
    private static readonly Regex OfferCountRegex = new(@"\+(\d+)\s*F", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<AkakceListingCard> Parse(string html, string siteOriginUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<AkakceListingCard>();
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var origin = TrimOrigin(siteOriginUrl);
        var nodes = doc.DocumentNode.SelectNodes("//ul[@id='PDL']/li[@data-pr]")
                    ?? doc.DocumentNode.SelectNodes("//li[@data-pr]");
        if (nodes == null)
        {
            return Array.Empty<AkakceListingCard>();
        }

        var byCode = new Dictionary<string, AkakceListingCard>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var card = ParseCard(node, origin);
            if (card != null)
            {
                byCode[card.ProductCode] = card;
            }
        }

        return byCode.Values.ToList();
    }

    public static int CountCandidateCards(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return 0;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc.DocumentNode.SelectNodes("//ul[@id='PDL']/li[@data-pr]")?.Count
               ?? doc.DocumentNode.SelectNodes("//li[@data-pr]")?.Count
               ?? 0;
    }

    private static AkakceListingCard? ParseCard(HtmlNode node, string origin)
    {
        var productCode = node.GetAttributeValue("data-pr", "").Trim();
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return null;
        }

        var anchor = FindProductDetailAnchor(node, productCode);
        if (anchor == null)
        {
            return null;
        }

        var href = HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", "")).Trim();
        if (!TryToAbsoluteUrl(href, origin, out var productUrl))
        {
            return null;
        }

        if (IsAkakceRedirectUrl(productUrl))
        {
            return null;
        }

        var titleNode = node.SelectSingleNode(".//h3[contains(@class,'pn_v8')]") ?? node.SelectSingleNode(".//h3");
        var title = HtmlEntity.DeEntitize(NormalizeWhitespace(titleNode?.InnerText));
        if (string.IsNullOrWhiteSpace(title))
        {
            title = HtmlEntity.DeEntitize(anchor.GetAttributeValue("title", "").Trim());
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var brand = node.GetAttributeValue("data-mk", null);
        if (string.IsNullOrWhiteSpace(brand) || brand == "-")
        {
            brand = null;
        }

        var img = node.SelectSingleNode(".//figure//img[@data-src or @src]")
                  ?? node.SelectSingleNode(".//img[@data-src or @src]");
        var imageUrl = PickImageUrl(img);
        imageUrl = NormalizeUrl(imageUrl, origin);

        decimal? discount = null;
        var discountNode = node.SelectSingleNode(".//span[contains(@class,'db_v9')]//i")
                           ?? node.SelectSingleNode(".//*[contains(text(),'%')]");
        if (discountNode != null)
        {
            var match = Regex.Match(HtmlEntity.DeEntitize(discountNode.InnerText), @"(\d+)", RegexOptions.CultureInvariant);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
            {
                discount = pct;
            }
        }

        decimal? bestPrice = null;
        var priceNode = node.SelectSingleNode(".//span[contains(@class,'pt_v9')]");
        if (priceNode != null)
        {
            bestPrice = AkakcePriceParser.TryParseTryAmount(HtmlEntity.DeEntitize(priceNode.InnerText));
        }

        int? offerCount = null;
        var offerText = HtmlEntity.DeEntitize(priceNode?.InnerText ?? node.InnerText);
        var offerMatch = OfferCountRegex.Match(offerText);
        if (offerMatch.Success && int.TryParse(offerMatch.Groups[1].Value, out var n))
        {
            offerCount = n + 1;
        }
        else if (offerText.Contains("TEK FIYAT", StringComparison.OrdinalIgnoreCase)
                 || offerText.Contains("TEK FİYAT", StringComparison.OrdinalIgnoreCase))
        {
            offerCount = 1;
        }

        return new AkakceListingCard
        {
            ProductCode = productCode,
            ProductUrl = productUrl,
            Title = title,
            BrandName = brand?.Trim(),
            ImageUrl = imageUrl,
            DiscountPercent = discount,
            BestPriceAmount = bestPrice,
            OfferCount = offerCount,
        };
    }

    private static HtmlNode? FindProductDetailAnchor(HtmlNode node, string productCode)
    {
        var anchors = node.SelectNodes(".//a[@href]") ?? new HtmlNodeCollection(node);

        return anchors.FirstOrDefault(a =>
                   LooksLikeProductDetailHref(a.GetAttributeValue("href", ""), productCode)
                   && HasClass(a, "iC"))
               ?? anchors.FirstOrDefault(a => LooksLikeProductDetailHref(a.GetAttributeValue("href", ""), productCode))
               ?? anchors.FirstOrDefault(a =>
                   HasClass(a, "iC")
                   && !IsAkakceRedirectHref(a.GetAttributeValue("href", "")))
               ?? anchors.FirstOrDefault(a =>
                   a.SelectSingleNode(".//h3") != null
                   && !IsAkakceRedirectHref(a.GetAttributeValue("href", "")));
    }

    private static bool LooksLikeProductDetailHref(string href, string productCode)
    {
        var decoded = HtmlEntity.DeEntitize(Uri.UnescapeDataString(href ?? string.Empty));
        return decoded.Contains("fiyati,", StringComparison.OrdinalIgnoreCase)
               && decoded.Contains(productCode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasClass(HtmlNode node, string className)
    {
        var raw = node.GetAttributeValue("class", "");
        return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(x => string.Equals(x, className, StringComparison.Ordinal));
    }

    private static bool IsAkakceRedirectHref(string href)
    {
        return IsAkakceRedirectUrl(HtmlEntity.DeEntitize(href ?? string.Empty));
    }

    private static bool IsAkakceRedirectUrl(string url)
    {
        return url.Contains("/c/?", StringComparison.OrdinalIgnoreCase)
               || url.Contains("/r/?", StringComparison.OrdinalIgnoreCase);
    }

    private static string? PickImageUrl(HtmlNode? img)
    {
        if (img == null)
        {
            return null;
        }

        var dataSrc = img.GetAttributeValue("data-src", null);
        if (!IsBadImageUrl(dataSrc))
        {
            return HtmlEntity.DeEntitize(dataSrc!.Trim());
        }

        var src = img.GetAttributeValue("src", null);
        return IsBadImageUrl(src) ? null : HtmlEntity.DeEntitize(src!.Trim());
    }

    internal static bool IsBadImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        var value = HtmlEntity.DeEntitize(url).Trim();
        return value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
               || value.EndsWith("/favicon.ico", StringComparison.OrdinalIgnoreCase)
               || value.Contains("akakce-logo", StringComparison.OrdinalIgnoreCase);
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
            && (abs.Scheme == Uri.UriSchemeHttps || abs.Scheme == Uri.UriSchemeHttp))
        {
            absolute = abs.ToString();
            return true;
        }

        if (!Uri.TryCreate(TrimOrigin(siteOriginUrl), UriKind.Absolute, out var origin))
        {
            return false;
        }

        if (Uri.TryCreate(origin, trimmed, out var combined))
        {
            absolute = combined.ToString();
            return true;
        }

        return false;
    }

    internal static string NormalizeWhitespace(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return Regex.Replace(raw, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static string? NormalizeUrl(string? raw, string origin)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = HtmlEntity.DeEntitize(raw).Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + trimmed;
        }

        return TryToAbsoluteUrl(trimmed, origin, out var abs) ? abs : trimmed;
    }

    private static string TrimOrigin(string siteOriginUrl)
    {
        return string.IsNullOrWhiteSpace(siteOriginUrl) ? string.Empty : siteOriginUrl.TrimEnd('/');
    }
}
