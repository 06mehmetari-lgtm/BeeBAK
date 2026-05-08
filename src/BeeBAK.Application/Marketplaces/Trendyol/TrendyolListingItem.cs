namespace BeeBAK.Marketplaces.Trendyol;

public sealed record TrendyolListingItem(
    string ExternalId,
    string Title,
    string ProductUrl,
    decimal Price,
    decimal? ListPrice,
    decimal? DiscountPercent,
    string? BrandName,
    string? MerchantExternalId,
    string? RawJson);
