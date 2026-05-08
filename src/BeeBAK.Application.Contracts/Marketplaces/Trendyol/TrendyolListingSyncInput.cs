using System;
using System.ComponentModel.DataAnnotations;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolListingSyncInput
{
    /// <summary>Free-text search sent as Trendyol <c>q</c>. Ignored when <see cref="EcMarketplaceCategoryId"/> is set.</summary>
    public string? SearchQuery { get; set; }

    /// <summary>Optional seeded category; resolved query uses category name, then slug, then external id.</summary>
    public Guid? EcMarketplaceCategoryId { get; set; }

    [Range(1, 50)]
    public int? MaxPages { get; set; }

    public bool ForceRefresh { get; set; }
}
