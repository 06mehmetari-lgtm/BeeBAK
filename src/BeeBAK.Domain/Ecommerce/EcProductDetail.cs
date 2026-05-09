using System;
using Volo.Abp.Domain.Entities;

namespace BeeBAK.Ecommerce;

/// <summary>Açıklama, özellik tablosu, satıcı ve değerlendirme özeti.</summary>
public class EcProductDetail : Entity<Guid>
{
    public Guid ProductId { get; protected set; }

    public string? DescriptionHtml { get; protected set; }

    /// <summary>Özellik listesi (JSON — pazaryerlerine göre farklı şemalar).</summary>
    public string? SpecificationsJson { get; protected set; }

    public decimal? RatingAverage { get; protected set; }

    public int? ReviewCount { get; protected set; }

    public string? SellerName { get; protected set; }

    public string? SellerScoreJson { get; protected set; }

    public virtual EcProduct Product { get; protected set; } = default!;

    protected EcProductDetail()
    {
    }

    public EcProductDetail(Guid id, Guid productId)
        : base(id)
    {
        ProductId = productId;
    }

    public void ApplyPdp(
        decimal? ratingAverage,
        int? reviewCount,
        string? sellerName,
        string? specificationsJson,
        string? sellerScoreJson)
    {
        RatingAverage = ratingAverage;
        ReviewCount = reviewCount;
        SellerName = sellerName;
        SpecificationsJson = specificationsJson;
        SellerScoreJson = sellerScoreJson;
    }
}
