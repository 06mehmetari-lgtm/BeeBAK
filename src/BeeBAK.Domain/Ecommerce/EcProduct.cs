using System;
using System.Collections.Generic;
using BeeBAK.Marketplaces;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BeeBAK.Ecommerce;

/// <summary>Tek ürün kaydı — fiyat ve detaylar ilişkili tablolarda.</summary>
public class EcProduct : FullAuditedAggregateRoot<Guid>
{
    public MarketplaceKind Marketplace { get; protected set; }

    public string ExternalProductId { get; protected set; } = default!;

    public string Title { get; protected set; } = default!;

    public string? BrandName { get; protected set; }

    public Guid? PrimaryCategoryId { get; protected set; }

    public string ProductUrl { get; protected set; } = default!;

    public string? Barcode { get; protected set; }

    public string? MerchantExternalId { get; protected set; }

    public DateTime? LastSyncedUtc { get; protected set; }

    public bool IsActive { get; protected set; }

    public virtual EcMarketplaceCategory? PrimaryCategory { get; protected set; }

    public virtual EcProductDetail? Detail { get; protected set; }

    public virtual ICollection<EcProductImage> Images { get; protected set; } = new List<EcProductImage>();

    public virtual ICollection<EcProductPriceSnapshot> PriceSnapshots { get; protected set; } =
        new List<EcProductPriceSnapshot>();

    protected EcProduct()
    {
    }

    public EcProduct(
        Guid id,
        MarketplaceKind marketplace,
        string externalProductId,
        string title,
        string productUrl,
        Guid? primaryCategoryId = null,
        string? brandName = null,
        string? barcode = null,
        string? merchantExternalId = null)
        : base(id)
    {
        Marketplace = marketplace;
        ExternalProductId = externalProductId;
        Title = title;
        ProductUrl = productUrl;
        PrimaryCategoryId = primaryCategoryId;
        BrandName = brandName;
        Barcode = barcode;
        MerchantExternalId = merchantExternalId;
        IsActive = true;
    }

    public void TouchSync(DateTime utcNow) => LastSyncedUtc = utcNow;

    public void SetActive(bool active) => IsActive = active;

    /// <summary>Updates listing fields after a marketplace sync (does not touch snapshots).</summary>
    public void ApplyListingSync(
        string title,
        string productUrl,
        Guid? primaryCategoryId = null,
        string? brandName = null,
        string? merchantExternalId = null)
    {
        Title = Check.NotNullOrWhiteSpace(title, nameof(title));
        ProductUrl = Check.NotNullOrWhiteSpace(productUrl, nameof(productUrl));
        PrimaryCategoryId = primaryCategoryId;
        BrandName = brandName;
        MerchantExternalId = merchantExternalId;
    }

    public void AddPriceSnapshot(EcProductPriceSnapshot snapshot)
    {
        PriceSnapshots.Add(Check.NotNull(snapshot, nameof(snapshot)));
    }
}
