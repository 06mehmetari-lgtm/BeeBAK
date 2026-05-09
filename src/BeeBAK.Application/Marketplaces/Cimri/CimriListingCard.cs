namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Cimri listeleme sayfasındaki tek ürün kartının ham özeti.</summary>
public sealed class CimriListingCard
{
    public string ContentId { get; set; } = default!;

    public string ProductUrl { get; set; } = default!;

    public string? CategorySlug { get; set; }

    public string Title { get; set; } = default!;

    public string? ImageUrl { get; set; }

    public decimal? DiscountPercent { get; set; }

    public int? OfferCount { get; set; }

    public string? BestMerchantName { get; set; }

    public decimal? BestPriceAmount { get; set; }

    public decimal? PreviousPriceAmount { get; set; }
}
