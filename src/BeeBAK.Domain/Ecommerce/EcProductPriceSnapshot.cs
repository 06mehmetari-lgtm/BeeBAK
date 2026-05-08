using System;
using Volo.Abp.Domain.Entities;

namespace BeeBAK.Ecommerce;

/// <summary>Anlık veya geçmiş fiyat satırı — kampanya/liste fiyatı.</summary>
public class EcProductPriceSnapshot : Entity<Guid>
{
    public Guid ProductId { get; protected set; }

    public decimal PriceAmount { get; protected set; }

    public string Currency { get; protected set; } = "TRY";

    public decimal? ListPriceAmount { get; protected set; }

    public decimal? DiscountPercent { get; protected set; }

    public DateTime ScrapedUtc { get; protected set; }

    /// <summary>Ham teklif verisi (isteğe bağlı debug).</summary>
    public string? RawOfferJson { get; protected set; }

    public virtual EcProduct Product { get; protected set; } = default!;

    protected EcProductPriceSnapshot()
    {
    }

    public EcProductPriceSnapshot(
        Guid id,
        Guid productId,
        decimal priceAmount,
        string currency,
        DateTime scrapedUtc,
        decimal? listPriceAmount = null,
        decimal? discountPercent = null,
        string? rawOfferJson = null)
        : base(id)
    {
        ProductId = productId;
        PriceAmount = priceAmount;
        Currency = currency;
        ScrapedUtc = scrapedUtc;
        ListPriceAmount = listPriceAmount;
        DiscountPercent = discountPercent;
        RawOfferJson = rawOfferJson;
    }
}
