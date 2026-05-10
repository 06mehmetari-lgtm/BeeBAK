using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BeeBAK.Marketplaces.Akakce;

/// <summary>Akakce fiyat dusen urunler listesinden gelen urun agregasi.</summary>
public class AkakceProduct : FullAuditedAggregateRoot<Guid>
{
    public string ProductCode { get; protected set; } = default!;
    public string ProductUrl { get; protected set; } = default!;
    public string Title { get; protected set; } = default!;
    public string? BrandName { get; protected set; }
    public string? PrimaryImageUrl { get; protected set; }
    public string? CategoryPath { get; protected set; }
    public decimal? DiscountPercent { get; protected set; }
    public decimal? BestPriceAmount { get; protected set; }
    public decimal? PreviousPriceAmount { get; protected set; }
    public int? OfferCount { get; protected set; }
    public bool IsActive { get; protected set; }
    public DateTime? LastSyncedUtc { get; protected set; }

    public virtual ICollection<AkakceOffer> Offers { get; protected set; } = new List<AkakceOffer>();

    protected AkakceProduct()
    {
    }

    public AkakceProduct(
        Guid id,
        string productCode,
        string productUrl,
        string title,
        string? brandName = null,
        string? primaryImageUrl = null)
        : base(id)
    {
        ProductCode = Check.NotNullOrWhiteSpace(productCode, nameof(productCode), maxLength: AkakceConsts.MaxProductCodeLength);
        ProductUrl = Check.NotNullOrWhiteSpace(productUrl, nameof(productUrl), maxLength: AkakceConsts.MaxProductUrlLength);
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), maxLength: AkakceConsts.MaxProductTitleLength);
        BrandName = brandName;
        PrimaryImageUrl = primaryImageUrl;
        IsActive = true;
    }

    public void ApplyListingSnapshot(
        string title,
        string productUrl,
        string? brandName,
        string? primaryImageUrl,
        decimal? discountPercent,
        int? offerCount,
        decimal? bestPriceAmount,
        decimal? previousPriceAmount,
        DateTime utcNow)
    {
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), maxLength: AkakceConsts.MaxProductTitleLength);
        ProductUrl = Check.NotNullOrWhiteSpace(productUrl, nameof(productUrl), maxLength: AkakceConsts.MaxProductUrlLength);
        BrandName = brandName;
        if (!string.IsNullOrWhiteSpace(primaryImageUrl))
        {
            PrimaryImageUrl = primaryImageUrl;
        }

        DiscountPercent = discountPercent;
        OfferCount = offerCount;
        BestPriceAmount = bestPriceAmount;
        PreviousPriceAmount = previousPriceAmount;
        IsActive = true;
        LastSyncedUtc = utcNow;
    }

    public void ApplyDetailSnapshot(
        string? categoryPath,
        string? brandName,
        string? primaryImageUrl,
        int? offerCount,
        DateTime utcNow)
    {
        if (!string.IsNullOrWhiteSpace(categoryPath))
        {
            CategoryPath = categoryPath;
        }

        if (!string.IsNullOrWhiteSpace(brandName))
        {
            BrandName = brandName;
        }

        if (!string.IsNullOrWhiteSpace(primaryImageUrl))
        {
            PrimaryImageUrl = primaryImageUrl;
        }

        if (offerCount.HasValue)
        {
            OfferCount = offerCount;
        }

        LastSyncedUtc = utcNow;
    }

    public void ClearOffers() => Offers.Clear();

    public void AddOffer(AkakceOffer offer)
    {
        Offers.Add(Check.NotNull(offer, nameof(offer)));
    }
}
