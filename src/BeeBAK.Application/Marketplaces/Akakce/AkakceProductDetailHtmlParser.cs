using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace BeeBAK.Marketplaces.Akakce;

public static class AkakceProductDetailHtmlParser
{
    public static AkakceProductDetailExtract? TryParse(string html, string productUrl, string productCode, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var titleNode = doc.DocumentNode.SelectSingleNode("//h1")
                        ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']");
        var title = titleNode?.Name.Equals("meta", StringComparison.OrdinalIgnoreCase) == true
            ? titleNode.GetAttributeValue("content", "")
            : titleNode?.InnerText;
        title = HtmlEntity.DeEntitize(AkakceListingHtmlParser.NormalizeWhitespace(title));
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var brandNode = doc.DocumentNode.SelectSingleNode("//h1/preceding::a[1]")
                        ?? doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/marka/')]");
        var brandName = HtmlEntity.DeEntitize(AkakceListingHtmlParser.NormalizeWhitespace(brandNode?.InnerText));
        if (string.IsNullOrWhiteSpace(brandName))
        {
            brandName = null;
        }

        var breadcrumbNodes = doc.DocumentNode.SelectNodes("//ol/li/a|//ul[contains(@id,'BC')]/li/a|//*[@id='BC_v8']//li/a");
        var categoryPath = breadcrumbNodes == null
            ? null
            : string.Join(" > ", breadcrumbNodes
                .Select(x => HtmlEntity.DeEntitize(AkakceListingHtmlParser.NormalizeWhitespace(x.InnerText)))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Skip(1));
        if (string.IsNullOrWhiteSpace(categoryPath))
        {
            categoryPath = null;
        }

        var imageUrl = ExtractProductImageUrl(doc, productUrl);
        var offers = ParseOffers(doc, productUrl, utcNow);

        return new AkakceProductDetailExtract
        {
            ProductCode = productCode,
            ProductUrl = productUrl,
            Title = title,
            BrandName = brandName,
            CategoryPath = categoryPath,
            PrimaryImageUrl = imageUrl,
            OfferCount = offers.Count > 0 ? offers.Count : null,
            Offers = offers,
        };
    }

    private static List<AkakceOfferExtract> ParseOffers(HtmlDocument doc, string productUrl, DateTime utcNow)
    {
        var candidates = doc.DocumentNode.SelectNodes(
            "//li[contains(.,'Sat\u0131c\u0131ya Git') or contains(.,'Saticiya Git')]"
            + "|//tr[contains(.,'Sat\u0131c\u0131ya Git') or contains(.,'Saticiya Git')]"
            + "|//div[(contains(.,'Sat\u0131c\u0131ya Git') or contains(.,'Saticiya Git')) and (.//a or .//button)]"
            + "|//*[@data-vendor-id and (.//a[@href] or .//button)]");

        var offers = new List<AkakceOfferExtract>();
        if (candidates == null)
        {
            return offers;
        }

        var displayOrder = 0;
        foreach (var row in candidates)
        {
            var text = HtmlEntity.DeEntitize(AkakceListingHtmlParser.NormalizeWhitespace(row.InnerText));
            if (string.IsNullOrWhiteSpace(text)
                || !text.Contains("TL", StringComparison.OrdinalIgnoreCase)
                || IsSummaryOrContainerText(text)
                || HasNestedOfferCandidate(row))
            {
                continue;
            }

            var price = AkakcePriceParser.TryParseFirstTryAmount(text);
            if (price == null)
            {
                continue;
            }

            var href = NormalizeUrl(
                row.SelectSingleNode(".//a[contains(.,'Sat\u0131c\u0131ya Git') or contains(.,'Saticiya Git')][@href]")
                    ?.GetAttributeValue("href", null)
                ?? row.SelectSingleNode(".//a[@href and (contains(@href,'/c/?') or contains(@href,'/r/?'))]")
                    ?.GetAttributeValue("href", null)
                ?? row.SelectSingleNode(".//a[@href]")?.GetAttributeValue("href", null),
                productUrl);
            var shippingText = ExtractShippingText(text);
            var stockText = ExtractStockText(text);
            var deliveryText = ExtractDeliveryText(text);
            var lastUpdatedText = ExtractLastUpdated(text);

            var merchant = ExtractMerchantName(row, text);
            if (string.IsNullOrWhiteSpace(merchant))
            {
                merchant = "Akakce Saticisi";
            }

            displayOrder++;
            offers.Add(new AkakceOfferExtract
            {
                MerchantName = merchant,
                MerchantLogoUrl = ExtractMerchantLogoUrl(row, productUrl),
                DisplayOrder = displayOrder,
                OfferTitle = ExtractOfferTitle(row),
                Price = price.Value,
                Currency = "TRY",
                ShippingText = shippingText,
                ShippingAmount = ExtractShippingAmount(shippingText),
                IsFreeShipping = shippingText?.Contains("\u00dccretsiz", StringComparison.OrdinalIgnoreCase)
                                 ?? shippingText?.Contains("Ucretsiz", StringComparison.OrdinalIgnoreCase),
                StockText = stockText,
                StockQuantity = ExtractStockQuantity(stockText),
                DeliveryText = deliveryText,
                LastUpdatedText = lastUpdatedText,
                LastUpdatedUtc = AkakcePriceParser.TryEstimateLastUpdated(text, utcNow),
                OfferUrl = href,
                MerchantProductUrl = href,
                SiteRedirectUrl = href,
            });
        }

        return offers
            .GroupBy(o => $"{o.MerchantName}|{o.Price}|{o.MerchantProductUrl}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(200)
            .ToList();
    }

    private static bool IsSummaryOrContainerText(string text)
    {
        return Regex.IsMatch(text, @"\b\d+\s+sat[\u0131i]c[\u0131i]\s+i[c\u00e7]inde\b", RegexOptions.IgnoreCase)
               || text.Contains("fiyat se\u00e7ene\u011fi", StringComparison.OrdinalIgnoreCase)
               || (text.Contains("fiyat se", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("ene", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasNestedOfferCandidate(HtmlNode row)
    {
        return row.Descendants()
            .Any(x => x != row
                      && IsOfferContainerName(x.Name)
                      && ContainsSellerLinkText(x)
                      && HtmlEntity.DeEntitize(AkakceListingHtmlParser.NormalizeWhitespace(x.InnerText))
                          .Contains("TL", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOfferContainerName(string name)
    {
        return string.Equals(name, "li", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "tr", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "div", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsSellerLinkText(HtmlNode node)
    {
        var text = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty);
        return text.Contains("Sat\u0131c\u0131ya Git", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Saticiya Git", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractMerchantName(HtmlNode row, string text)
    {
        var afterSeller = Regex.Match(text, "Sat[\\u0131i]c[\\u0131i]:\\s*(?<m>.+)$", RegexOptions.IgnoreCase);
        if (afterSeller.Success)
        {
            var value = afterSeller.Groups["m"].Value.Trim();
            value = Regex.Split(value, "\\s+(Sat\\u0131c\\u0131ya Git|Saticiya Git|\\u00dcr\\u00fcn \\u00d6zellikleri|Fiyat De\\u011fi\\u015fimi|Alarm kur|Son g\\u00fcncelleme)", RegexOptions.IgnoreCase)[0].Trim();
            value = value.Trim('/', ' ', ':');
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        var merchantNode = row.SelectSingleNode(".//*[contains(@class,'seller') or contains(@class,'merchant') or contains(@class,'shop')][normalize-space()]");
        var merchantText = HtmlEntity.DeEntitize(AkakceListingHtmlParser.NormalizeWhitespace(merchantNode?.InnerText));
        if (!string.IsNullOrWhiteSpace(merchantText) && !merchantText.Contains("TL", StringComparison.OrdinalIgnoreCase))
        {
            return merchantText;
        }

        var logoAlt = row.SelectSingleNode(".//img[@alt]")?.GetAttributeValue("alt", null);
        if (!string.IsNullOrWhiteSpace(logoAlt)
            && logoAlt.Length <= 40
            && !logoAlt.Contains("logo", StringComparison.OrdinalIgnoreCase))
        {
            return HtmlEntity.DeEntitize(logoAlt.Trim());
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var tail = string.Join(" ", parts.TakeLast(Math.Min(3, parts.Length)));
        tail = Regex.Replace(tail, @"^\W+|\W+$", "");
        return string.IsNullOrWhiteSpace(tail) ? null : tail;
    }

    private static string? ExtractOfferTitle(HtmlNode row)
    {
        var title = row.SelectSingleNode(".//h3|.//h4|.//*[contains(@class,'pn_v8')]")?.InnerText;
        title = HtmlEntity.DeEntitize(AkakceListingHtmlParser.NormalizeWhitespace(title));
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string? ExtractMerchantLogoUrl(HtmlNode row, string productUrl)
    {
        var img = row.SelectSingleNode(".//img[@data-src or @src]");
        var raw = img?.GetAttributeValue("data-src", null);
        if (AkakceListingHtmlParser.IsBadImageUrl(raw))
        {
            raw = img?.GetAttributeValue("src", null);
        }

        return NormalizeUrl(raw, productUrl);
    }

    private static string? ExtractProductImageUrl(HtmlDocument doc, string productUrl)
    {
        var raw = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
            ?.GetAttributeValue("content", null);

        if (AkakceListingHtmlParser.IsBadImageUrl(raw))
        {
            raw = doc.DocumentNode.SelectSingleNode(
                    "//img[not(contains(@src,'akakce-logo')) and not(contains(@data-src,'akakce-logo')) and (@data-src or @src)]")
                ?.GetAttributeValue("data-src", null);
        }

        if (AkakceListingHtmlParser.IsBadImageUrl(raw))
        {
            raw = doc.DocumentNode.SelectSingleNode(
                    "//img[not(contains(@src,'akakce-logo')) and not(contains(@data-src,'akakce-logo')) and (@data-src or @src)]")
                ?.GetAttributeValue("src", null);
        }

        return NormalizeUrl(raw, productUrl);
    }

    private static string? ExtractShippingText(string text)
    {
        var match = Regex.Match(text, "(?<v>(\\u00dccretsiz|Ucretsiz)\\s+kargo|[+\\-]?\\s*\\d{1,3}(?:\\.\\d{3})*(?:,\\d{1,2})?\\s*TL\\s+kargo)", RegexOptions.IgnoreCase);
        return match.Success ? Clean(match.Groups["v"].Value) : null;
    }

    private static decimal? ExtractShippingAmount(string? shippingText)
    {
        if (string.IsNullOrWhiteSpace(shippingText))
        {
            return null;
        }

        if (shippingText.Contains("\u00dccretsiz", StringComparison.OrdinalIgnoreCase)
            || shippingText.Contains("Ucretsiz", StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        return AkakcePriceParser.TryParseFirstTryAmount(shippingText);
    }

    private static string? ExtractStockText(string text)
    {
        var match = Regex.Match(text, @"Stokta\s+(?<n>\d+\+?)\s+adet", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return Clean(match.Value);
        }

        return text.Contains("Stokta", StringComparison.OrdinalIgnoreCase) ? "Stokta" : null;
    }

    private static int? ExtractStockQuantity(string? stockText)
    {
        var match = Regex.Match(stockText ?? string.Empty, @"(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : null;
    }

    private static string? ExtractDeliveryText(string text)
    {
        var match = Regex.Match(text, "(?<v>Yar\\u0131n\\s+kargoda|\\d+\\s+i\\u015f\\s+g\\u00fcn\\u00fc|\\d+\\s+is\\s+gunu)", RegexOptions.IgnoreCase);
        return match.Success ? Clean(match.Groups["v"].Value) : null;
    }

    private static string? ExtractLastUpdated(string text)
    {
        var match = Regex.Match(text, "Son\\s+(?:g\\u00fcncelleme|guncelleme):\\s*(?<v>(?:\\d+\\s+dakika\\s+(?:\\u00f6nce|once))|(?:(?:Bug\\u00fcn|Bugun)\\s+\\d{1,2}:\\d{2})|(?:\\d+\\s+dk\\s+(?:\\u00f6nce|once)))", RegexOptions.IgnoreCase);
        return match.Success ? "Son g\u00fcncelleme: " + Clean(match.Groups["v"].Value) : null;
    }

    private static string Clean(string value)
    {
        return HtmlEntity.DeEntitize(AkakceListingHtmlParser.NormalizeWhitespace(value));
    }

    private static string? NormalizeUrl(string? raw, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = HtmlEntity.DeEntitize(raw).Trim();
        if (AkakceListingHtmlParser.IsBadImageUrl(value))
        {
            return null;
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + value;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            && Uri.TryCreate(baseUri, value, out var combined))
        {
            return combined.ToString();
        }

        return value;
    }
}

public sealed class AkakceProductDetailExtract
{
    public string ProductCode { get; set; } = default!;
    public string ProductUrl { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? BrandName { get; set; }
    public string? CategoryPath { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public int? OfferCount { get; set; }
    public List<AkakceOfferExtract> Offers { get; set; } = new();
}

public sealed class AkakceOfferExtract
{
    public string MerchantName { get; set; } = default!;
    public string? MerchantLogoUrl { get; set; }
    public int DisplayOrder { get; set; }
    public string? OfferTitle { get; set; }
    public string? SellerName { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public string? ShippingText { get; set; }
    public decimal? ShippingAmount { get; set; }
    public bool? IsFreeShipping { get; set; }
    public string? StockText { get; set; }
    public int? StockQuantity { get; set; }
    public string? DeliveryText { get; set; }
    public string? LastUpdatedText { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
    public string? OfferUrl { get; set; }
    public string? MerchantProductUrl { get; set; }
    public string? SiteRedirectUrl { get; set; }
}
