using System;
using Volo.Abp.Application.Dtos;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceOfferDto : EntityDto<Guid>
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
    public decimal? ShippingAmount { get; set; }
    public bool? IsFreeShipping { get; set; }
    public string? StockText { get; set; }
    public int? StockQuantity { get; set; }
    public string? DeliveryText { get; set; }
    public string? LastUpdatedText { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
    public string? OfferUrl { get; set; }
    public string? MerchantProductUrl { get; set; }
    public string? SiteRedirectUrl { get; set; }
    public DateTime ScrapedUtc { get; set; }
}
