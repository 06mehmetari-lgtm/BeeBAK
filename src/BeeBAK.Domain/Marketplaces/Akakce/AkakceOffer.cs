using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace BeeBAK.Marketplaces.Akakce;

/// <summary>Akakce urun detay sayfasinda tek saticidan gelen fiyat snapshot'i.</summary>
public class AkakceOffer : Entity<Guid>
{
    public Guid ProductId { get; protected set; }
    public Guid MerchantId { get; protected set; }
    public int DisplayOrder { get; protected set; }
    public string? OfferTitle { get; protected set; }
    public string? SellerName { get; protected set; }
    public decimal Price { get; protected set; }
    public string Currency { get; protected set; } = "TRY";
    public string? ShippingText { get; protected set; }
    public decimal? ShippingAmount { get; protected set; }
    public bool? IsFreeShipping { get; protected set; }
    public string? StockText { get; protected set; }
    public int? StockQuantity { get; protected set; }
    public string? DeliveryText { get; protected set; }
    public string? LastUpdatedText { get; protected set; }
    public DateTime? LastUpdatedUtc { get; protected set; }
    public string? OfferUrl { get; protected set; }
    public string? MerchantProductUrl { get; protected set; }
    public string? SiteRedirectUrl { get; protected set; }
    public DateTime ScrapedUtc { get; protected set; }

    public virtual AkakceProduct Product { get; protected set; } = default!;
    public virtual AkakceMerchant Merchant { get; protected set; } = default!;

    protected AkakceOffer()
    {
    }

    public AkakceOffer(
        Guid id,
        Guid productId,
        Guid merchantId,
        decimal price,
        DateTime scrapedUtc,
        int displayOrder = 0,
        string currency = "TRY")
        : base(id)
    {
        ProductId = productId;
        MerchantId = merchantId;
        Price = Check.Range(price, nameof(price), 0m, 99_999_999m);
        Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency;
        ScrapedUtc = scrapedUtc;
        DisplayOrder = displayOrder;
    }

    public void SetMetadata(
        string? offerTitle,
        string? sellerName,
        string? shippingText,
        decimal? shippingAmount,
        bool? isFreeShipping,
        string? stockText,
        int? stockQuantity,
        string? deliveryText,
        string? lastUpdatedText,
        DateTime? lastUpdatedUtc,
        string? offerUrl,
        string? merchantProductUrl,
        string? siteRedirectUrl)
    {
        OfferTitle = Truncate(offerTitle, AkakceConsts.MaxOfferTitleLength);
        SellerName = Truncate(sellerName, AkakceConsts.MaxSellerNameLength);
        ShippingText = Truncate(shippingText, AkakceConsts.MaxOfferTextLength);
        ShippingAmount = shippingAmount;
        IsFreeShipping = isFreeShipping;
        StockText = Truncate(stockText, AkakceConsts.MaxOfferTextLength);
        StockQuantity = stockQuantity;
        DeliveryText = Truncate(deliveryText, AkakceConsts.MaxOfferTextLength);
        LastUpdatedText = Truncate(lastUpdatedText, AkakceConsts.MaxOfferTextLength);
        LastUpdatedUtc = lastUpdatedUtc;
        OfferUrl = Truncate(offerUrl, AkakceConsts.MaxOfferUrlLength);
        MerchantProductUrl = Truncate(merchantProductUrl, AkakceConsts.MaxMerchantProductUrlLength);
        SiteRedirectUrl = Truncate(siteRedirectUrl, AkakceConsts.MaxOfferUrlLength);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
