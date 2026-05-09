namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Cimri.com fiyat karşılaştırma entegrasyonu için sabitler.</summary>
public static class CimriConsts
{
    public const int MaxProductTitleLength = 512;

    public const int MaxBrandNameLength = 256;

    public const int MaxCategoryPathLength = 1024;

    public const int MaxImageUrlLength = 1024;

    public const int MaxProductUrlLength = 1024;

    public const int MaxContentIdLength = 64;

    public const int MaxMerchantNameLength = 160;

    public const int MaxMerchantSlugLength = 160;

    public const int MaxMerchantLogoUrlLength = 1024;

    public const int MaxOfferTitleLength = 512;

    public const int MaxSellerNameLength = 256;

    public const int MaxOfferTextLength = 1024;

    public const int MaxOfferUrlLength = 2048;

    public const int MaxMerchantProductUrlLength = 2048;

    public const int MaxMerchantProductIdLength = 256;

    public const int MaxBadgeLength = 128;

    /// <summary>Cimri ürün URL'si bu site dışından gelmemelidir.</summary>
    public const string ExpectedHost = "www.cimri.com";

    /// <summary>İndirimli ürünler listeleme yolu.</summary>
    public const string DiscountedListingPath = "/indirimli-urunler";
}
