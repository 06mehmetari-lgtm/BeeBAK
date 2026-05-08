namespace BeeBAK.Marketplaces.Trendyol;

public sealed record TrendyolNavigationSectionParsed(
    string ExternalCategoryId,
    string DisplayName,
    string? Slug,
    string RelativeHref);
