using System;
using System.Collections.Generic;
using BeeBAK.Marketplaces.Cimri;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Cimri'de listelenen tek ürün — N adet mağaza teklifi (CimriOffer) bu agrega ile bağlıdır.</summary>
public class CimriProduct : FullAuditedAggregateRoot<Guid>
{
    /// <summary>Cimri ürün URL'si sonundaki numerik içerik kimliği (örn. <c>620147472</c>).</summary>
    public string ContentId { get; protected set; } = default!;

    /// <summary>Tam ürün detay URL'si (<c>https://www.cimri.com/...,{ContentId}</c>).</summary>
    public string ProductUrl { get; protected set; } = default!;

    /// <summary>URL'deki kategori slug'ı (örn. <c>sampuan-ve-sac-kremi</c>).</summary>
    public string? PrimaryCategorySlug { get; protected set; }

    /// <summary>Detay sayfasındaki breadcrumb (' > ' birleşik).</summary>
    public string? CategoryPath { get; protected set; }

    public string Title { get; protected set; } = default!;

    public string? BrandName { get; protected set; }

    public string? PrimaryImageUrl { get; protected set; }

    /// <summary>Listeleme veya detay sayfasında okunan toplam teklif sayısı (yalnız bilgi amaçlı).</summary>
    public int? TotalOfferCount { get; protected set; }

    /// <summary>Cimri'nin özet indirim oranı (örn. liste kartında %11 yazıyorsa 11).</summary>
    public decimal? DiscountPercent { get; protected set; }

    public decimal? BestPriceAmount { get; protected set; }

    public string? BestPriceMerchantName { get; protected set; }

    public decimal? PreviousPriceAmount { get; protected set; }

    public bool IsActive { get; protected set; }

    public DateTime? LastSyncedUtc { get; protected set; }

    public virtual ICollection<CimriOffer> Offers { get; protected set; } = new List<CimriOffer>();

    protected CimriProduct()
    {
    }

    public CimriProduct(
        Guid id,
        string contentId,
        string productUrl,
        string title,
        string? primaryCategorySlug = null,
        string? brandName = null,
        string? primaryImageUrl = null)
        : base(id)
    {
        ContentId = Check.NotNullOrWhiteSpace(contentId, nameof(contentId), maxLength: CimriConsts.MaxContentIdLength);
        ProductUrl = Check.NotNullOrWhiteSpace(productUrl, nameof(productUrl), maxLength: CimriConsts.MaxProductUrlLength);
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), maxLength: CimriConsts.MaxProductTitleLength);
        PrimaryCategorySlug = primaryCategorySlug;
        BrandName = brandName;
        PrimaryImageUrl = primaryImageUrl;
        IsActive = true;
    }

    public void ApplyListingSnapshot(
        string title,
        string productUrl,
        string? primaryCategorySlug,
        string? brandName,
        string? primaryImageUrl,
        decimal? discountPercent,
        int? totalOfferCount,
        decimal? bestPriceAmount,
        string? bestPriceMerchantName,
        decimal? previousPriceAmount,
        DateTime utcNow)
    {
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), maxLength: CimriConsts.MaxProductTitleLength);
        ProductUrl = Check.NotNullOrWhiteSpace(productUrl, nameof(productUrl), maxLength: CimriConsts.MaxProductUrlLength);
        PrimaryCategorySlug = primaryCategorySlug;
        BrandName = brandName;
        if (!string.IsNullOrWhiteSpace(primaryImageUrl))
        {
            PrimaryImageUrl = primaryImageUrl;
        }
        DiscountPercent = discountPercent;
        TotalOfferCount = totalOfferCount;
        BestPriceAmount = bestPriceAmount;
        BestPriceMerchantName = bestPriceMerchantName;
        PreviousPriceAmount = previousPriceAmount;
        IsActive = true;
        LastSyncedUtc = utcNow;
    }

    public void ApplyDetailSnapshot(
        string? categoryPath,
        string? brandName,
        string? primaryImageUrl,
        int? totalOfferCount,
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

        if (totalOfferCount.HasValue)
        {
            TotalOfferCount = totalOfferCount;
        }

        LastSyncedUtc = utcNow;
    }

    public void TouchSync(DateTime utcNow) => LastSyncedUtc = utcNow;

    public void SetActive(bool active) => IsActive = active;

    public void ClearOffers()
    {
        Offers.Clear();
    }

    public void AddOffer(CimriOffer offer)
    {
        Offers.Add(Check.NotNull(offer, nameof(offer)));
    }
}
