using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri ürün detay (PDP) HTML'ini parse eder: başlık, marka, breadcrumb, görsel, toplam teklif sayısı
/// ve <c>section#fiyatlar</c> içindeki tüm offer kartları.
/// </summary>
public static class CimriProductDetailHtmlParser
{
    private static readonly Regex OfferCountRegex = new(@"(\d+)\s+Adet\s+Fiyat", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static CimriProductDetailExtract? TryParse(string html, string productUrl, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var titleNode = doc.DocumentNode.SelectSingleNode("//h1[contains(@class,'mEY_q')]")
                     ?? doc.DocumentNode.SelectSingleNode("//h1");
        if (titleNode == null)
        {
            return null;
        }

        var title = HtmlEntity.DeEntitize(NormalizeWhitespace(titleNode.InnerText));
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        if (!CimriListingHtmlParser.TryExtractContentId(productUrl, out var contentId))
        {
            return null;
        }

        var brandNode = titleNode.SelectSingleNode(".//a[contains(@href,'/marka/')]");
        var brandName = brandNode != null ? HtmlEntity.DeEntitize(brandNode.InnerText.Trim()) : null;

        var slug = CimriListingHtmlParser.ExtractCategorySlugFromUrl(productUrl);

        var breadcrumbItems = doc.DocumentNode.SelectNodes("//ul[@id='breadcrumb']/li/a")
                              ?? new HtmlNodeCollection(doc.DocumentNode);
        var pathParts = breadcrumbItems
            .Select(a => HtmlEntity.DeEntitize(NormalizeWhitespace(a.InnerText)))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        var categoryPath = pathParts.Count > 0 ? string.Join(" › ", pathParts) : null;

        var imageNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'keen-slider__slide')]//img[@src]")
                     ?? doc.DocumentNode.SelectSingleNode("//img[@class='fFv_y']")
                     ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'Ix2L1')]//img");
        var imageUrl = imageNode?.GetAttributeValue("src", null);
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            imageUrl = imageNode?.GetAttributeValue("srcset", null)?.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().Split(' ').FirstOrDefault();
        }

        int? totalOfferCount = null;
        var offerCountNode = doc.DocumentNode.SelectSingleNode("//section[@id='fiyatlar']//span[contains(@class,'ranLr')]");
        if (offerCountNode != null)
        {
            var match = OfferCountRegex.Match(HtmlEntity.DeEntitize(offerCountNode.InnerText));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
            {
                totalOfferCount = n;
            }
        }

        var offers = ParseOffers(doc, utcNow);

        return new CimriProductDetailExtract
        {
            ContentId = contentId,
            ProductUrl = productUrl,
            Title = title,
            BrandName = brandName,
            CategorySlug = slug,
            CategoryPath = categoryPath,
            PrimaryImageUrl = imageUrl,
            TotalOfferCount = totalOfferCount,
            Offers = offers,
        };
    }

    private static List<CimriOfferExtract> ParseOffers(HtmlDocument doc, DateTime utcNow)
    {
        var offers = new List<CimriOfferExtract>();

        var offerCards = doc.DocumentNode.SelectNodes("//section[@id='fiyatlar']//div[contains(@class,'o1fRW')]")
                      ?? doc.DocumentNode.SelectNodes("//div[@data-offer]");

        if (offerCards == null)
        {
            return offers;
        }

        var displayOrder = 0;
        foreach (var card in offerCards)
        {
            displayOrder++;

            var dataOffer = card.GetAttributeValue("data-offer", null);
            int? dataOfferOrder = int.TryParse(dataOffer, out var ofVal) ? ofVal : null;

            var merchantImg = card.SelectSingleNode(".//div[contains(@class,'LUOwR')]//img[@alt]");
            var merchantName = HtmlEntity.DeEntitize(merchantImg?.GetAttributeValue("alt", "")?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(merchantName))
            {
                continue;
            }

            var merchantLogo = merchantImg?.GetAttributeValue("src", null);

            var offerTitleNode = card.SelectSingleNode(".//div[contains(@class,'ZTKTN')]");
            var offerTitle = offerTitleNode != null
                ? HtmlEntity.DeEntitize(NormalizeWhitespace(offerTitleNode.InnerText))
                : null;

            var promotionNode = card.SelectSingleNode(".//div[contains(@class,'EKMIN')]");
            var promotion = promotionNode != null
                ? HtmlEntity.DeEntitize(NormalizeWhitespace(promotionNode.InnerText))
                : null;

            var sellerNode = card.SelectSingleNode(".//div[contains(@class,'jTeJH')]/div[contains(@class,'zp61l')]");
            var sellerName = sellerNode != null
                ? HtmlEntity.DeEntitize(NormalizeWhitespace(sellerNode.InnerText))
                : null;

            var priceNode = card.SelectSingleNode(".//div[contains(@class,'rTdMX')]");
            var priceAmount = priceNode != null ? CimriPriceParser.TryParseTryAmount(priceNode.InnerText) : null;
            if (priceAmount == null)
            {
                continue;
            }

            // sS0lR span -> [shipping, lastUpdated] sırasıyla
            var spanInfo = card.SelectNodes(".//div[contains(@class,'sS0lR')]/span");
            string? shipping = null;
            string? lastUpdatedText = null;
            if (spanInfo != null)
            {
                if (spanInfo.Count >= 1)
                {
                    shipping = HtmlEntity.DeEntitize(NormalizeWhitespace(spanInfo[0].InnerText));
                }

                if (spanInfo.Count >= 2)
                {
                    lastUpdatedText = HtmlEntity.DeEntitize(NormalizeWhitespace(spanInfo[1].InnerText));
                }
            }

            var lastUpdatedUtc = CimriPriceParser.TryEstimateLastUpdated(lastUpdatedText, utcNow);

            var installmentNode = card.SelectSingleNode(".//span[contains(@class,'baNkj') and contains(@class,'omr7X')]/span[contains(@class,'XEyUx')]")
                               ?? card.SelectSingleNode(".//span[contains(@class,'baNkj')]");
            var installment = installmentNode != null
                ? HtmlEntity.DeEntitize(NormalizeWhitespace(installmentNode.InnerText))
                : null;
            installment = StripBadgeText(installment);

            var scoreNode = card.SelectSingleNode(".//span[contains(@class,'kjHjJ')]");
            var merchantScore = scoreNode != null
                ? CimriPriceParser.TryParseScore(NormalizeWhitespace(scoreNode.InnerText))
                : null;

            var sponsoredBadge = card.SelectSingleNode(".//span[contains(@class,'myMAI')]")
                              ?? card.SelectSingleNode(".//span[contains(.,'Reklam') and contains(@class,'jjtPe')]");
            var isSponsored = sponsoredBadge != null;

            var cheapestBadge = card.SelectSingleNode(".//span[contains(@class,'YUB2l')]")
                              ?? card.SelectSingleNode(".//span[contains(.,'En Ucuz')]");
            var isCheapest = cheapestBadge != null;

            var yearsBadge = card.SelectSingleNode(".//span[contains(@class,'KCc8F')]/span[contains(@class,'Ry7zQ')]")
                          ?? card.SelectSingleNode(".//span[contains(.,'Yıldır Cimri')]");
            var yearsOnCimri = yearsBadge != null
                ? CimriPriceParser.TryParseYearsBadge(NormalizeWhitespace(yearsBadge.InnerText))
                : null;

            var offerUrl = ExtractOfferUrl(card);

            offers.Add(new CimriOfferExtract
            {
                MerchantName = merchantName,
                MerchantLogoUrl = merchantLogo,
                DisplayOrder = dataOfferOrder ?? displayOrder,
                OfferTitle = offerTitle,
                SellerName = sellerName,
                Price = priceAmount.Value,
                Currency = "TRY",
                ShippingText = shipping,
                PromotionText = promotion,
                LastUpdatedText = lastUpdatedText,
                LastUpdatedUtc = lastUpdatedUtc,
                InstallmentBadge = installment,
                MerchantScore = merchantScore,
                IsSponsored = isSponsored,
                IsCheapest = isCheapest,
                YearsOnCimri = yearsOnCimri,
                OfferUrl = offerUrl,
            });
        }

        return offers;
    }

    private static string? ExtractOfferUrl(HtmlNode card)
    {
        // Cimri'nin offer kartı tıklamayı genelde JS yapıyor; href varsa onu al.
        var anchor = card.SelectSingleNode(".//a[@href]");
        return anchor?.GetAttributeValue("href", null);
    }

    private static string? StripBadgeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = NormalizeWhitespace(text);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeWhitespace(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        return Regex.Replace(raw, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }
}

public sealed class CimriProductDetailExtract
{
    public string ContentId { get; set; } = default!;

    public string ProductUrl { get; set; } = default!;

    public string Title { get; set; } = default!;

    public string? BrandName { get; set; }

    public string? CategorySlug { get; set; }

    public string? CategoryPath { get; set; }

    public string? PrimaryImageUrl { get; set; }

    public int? TotalOfferCount { get; set; }

    public List<CimriOfferExtract> Offers { get; set; } = new();
}

public sealed class CimriOfferExtract
{
    public string MerchantName { get; set; } = default!;

    public string? MerchantLogoUrl { get; set; }

    public int DisplayOrder { get; set; }

    public string? OfferTitle { get; set; }

    public string? SellerName { get; set; }

    public decimal Price { get; set; }

    public string Currency { get; set; } = "TRY";

    public string? ShippingText { get; set; }

    public string? PromotionText { get; set; }

    public string? LastUpdatedText { get; set; }

    public DateTime? LastUpdatedUtc { get; set; }

    public string? InstallmentBadge { get; set; }

    public decimal? MerchantScore { get; set; }

    public bool IsSponsored { get; set; }

    public bool IsCheapest { get; set; }

    public int? YearsOnCimri { get; set; }

    public string? OfferUrl { get; set; }
}
