namespace BeeBAK.Marketplaces.Akakce;

public sealed class AkakceListingCard
{
    public string ProductCode { get; set; } = default!;
    public string ProductUrl { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? BrandName { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? BestPriceAmount { get; set; }
    public decimal? PreviousPriceAmount { get; set; }
    public int? OfferCount { get; set; }
}
