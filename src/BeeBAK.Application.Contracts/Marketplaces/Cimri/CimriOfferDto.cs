using System;
using Volo.Abp.Application.Dtos;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriOfferDto : EntityDto<Guid>
{
    public Guid ProductId { get; set; }

    public Guid MerchantId { get; set; }

    public string MerchantName { get; set; } = default!;

    public string? MerchantLogoUrl { get; set; }

    public int DisplayOrder { get; set; }

    public string? OfferTitle { get; set; }

    public string? SellerName { get; set; }

    public decimal Price { get; set; }

    public string Currency { get; set; } = "TRY";

    public string? ShippingText { get; set; }

    public string? PromotionText { get; set; }

    public string? LastUpdatedText { get; set; }

    public DateTime? LastUpdatedUtc { get; set; }

    public string? InstallmentBadge { get; set; }

    public decimal? MerchantScore { get; set; }

    public bool IsSponsored { get; set; }

    public bool IsCheapest { get; set; }

    public int? YearsOnCimri { get; set; }

    public string? OfferUrl { get; set; }

    /// <summary>Cimri redirect'i takip edildikten sonra ulaşılan, mağazanın asıl ürün sayfası URL'si.</summary>
    public string? MerchantProductUrl { get; set; }

    /// <summary>Mağaza tarafındaki ürün id/SKU (Hepsiburada SKU, Trendyol contentId, Amazon ASIN, vb.).</summary>
    public string? MerchantProductId { get; set; }

    public DateTime ScrapedUtc { get; set; }
}
