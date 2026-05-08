using System;
using System.Collections.Generic;
using BeeBAK.Marketplaces;
using Volo.Abp.Domain.Entities.Auditing;

namespace BeeBAK.Ecommerce;

/// <summary>Pazaryerinden gelen kategori ağacı (hiyerarşik).</summary>
public class EcMarketplaceCategory : FullAuditedAggregateRoot<Guid>
{
    public MarketplaceKind Marketplace { get; protected set; }

    /// <summary>HB/TY tarafındaki kategori kimliği (string — API/HTML kaynaklı).</summary>
    public string ExternalCategoryId { get; protected set; } = default!;

    public Guid? ParentId { get; protected set; }

    public string Name { get; protected set; } = default!;

    public string? Slug { get; protected set; }

    public string? CategoryUrl { get; protected set; }

    /// <summary>Ham özellikler / breadcrumb / ek meta.</summary>
    public string? ExtraAttributesJson { get; protected set; }

    public virtual EcMarketplaceCategory? Parent { get; protected set; }

    public virtual ICollection<EcMarketplaceCategory> Children { get; protected set; } = new List<EcMarketplaceCategory>();

    public virtual ICollection<EcProduct> Products { get; protected set; } = new List<EcProduct>();

    protected EcMarketplaceCategory()
    {
    }

    public EcMarketplaceCategory(
        Guid id,
        MarketplaceKind marketplace,
        string externalCategoryId,
        string name,
        Guid? parentId = null,
        string? slug = null,
        string? categoryUrl = null,
        string? extraAttributesJson = null)
        : base(id)
    {
        Marketplace = marketplace;
        ExternalCategoryId = externalCategoryId;
        Name = name;
        ParentId = parentId;
        Slug = slug;
        CategoryUrl = categoryUrl;
        ExtraAttributesJson = extraAttributesJson;
    }
}
