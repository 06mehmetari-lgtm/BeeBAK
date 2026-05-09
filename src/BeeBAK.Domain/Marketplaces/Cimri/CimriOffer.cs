using System;
using BeeBAK.Marketplaces.Cimri;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Tek bir Cimri ürünü için tek mağazadan gelen fiyat teklifi snapshot'ı.</summary>
public class CimriOffer : Entity<Guid>
{
    public Guid ProductId { get; protected set; }

    public Guid MerchantId { get; protected set; }

    /// <summary>Ürün detay sayfasında teklifin sıralandığı pozisyon (1 = en üst, reklam dahil).</summary>
    public int DisplayOrder { get; protected set; }

    /// <summary>Mağaza tarafının ürün başlığı (her offer farklı yazabiliyor).</summary>
    public string? OfferTitle { get; protected set; }

    /// <summary>"jTeJH > zp61l" — mağaza altındaki satıcı ismi (varsa).</summary>
    public string? SellerName { get; protected set; }

    /// <summary>Teklif fiyatı (TL).</summary>
    public decimal Price { get; protected set; }

    public string Currency { get; protected set; } = "TRY";

    /// <summary>Teklif sayfasındaki "Ücretsiz, 1 Günde Kargo" gibi kargo açıklaması.</summary>
    public string? ShippingText { get; protected set; }

    /// <summary>EKMIN — promosyon/banner metni (örn. "Mayıs Fırsatları").</summary>
    public string? PromotionText { get; protected set; }

    /// <summary>"11 dk önce güncellendi" — ham metin saklanır, parse edilebiliyorsa <see cref="LastUpdatedUtc"/> doldurulur.</summary>
    public string? LastUpdatedText { get; protected set; }

    public DateTime? LastUpdatedUtc { get; protected set; }

    /// <summary>"Peşin Fiyatına 6 Taksit" gibi kart üstü etiket.</summary>
    public string? InstallmentBadge { get; protected set; }

    /// <summary>Mağaza puanı varsa (Civilim 8,4 vs.).</summary>
    public decimal? MerchantScore { get; protected set; }

    /// <summary>"Reklam" kartı mı?</summary>
    public bool IsSponsored { get; protected set; }

    /// <summary>"En Ucuz" rozeti.</summary>
    public bool IsCheapest { get; protected set; }

    /// <summary>Cimri'nin "X Yıldır Cimri'de" rozeti (varsa).</summary>
    public int? YearsOnCimri { get; protected set; }

    /// <summary>Teklif kartından çıkarılan tıklanır URL — Cimri tarafının redirect linki.</summary>
    public string? OfferUrl { get; protected set; }

    public DateTime ScrapedUtc { get; protected set; }

    public virtual CimriProduct Product { get; protected set; } = default!;

    public virtual CimriMerchant Merchant { get; protected set; } = default!;

    protected CimriOffer()
    {
    }

    public CimriOffer(
        Guid id,
        Guid productId,
        Guid merchantId,
        decimal price,
        DateTime scrapedUtc,
        int displayOrder = 0,
        string currency = "TRY")
        : base(id)
    {
        ProductId = productId;
        MerchantId = merchantId;
        Price = Check.Range(price, nameof(price), 0m, 99_999_999m);
        Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency;
        ScrapedUtc = scrapedUtc;
        DisplayOrder = displayOrder;
    }

    public void SetMetadata(
        string? offerTitle,
        string? sellerName,
        string? shippingText,
        string? promotionText,
        string? lastUpdatedText,
        DateTime? lastUpdatedUtc,
        string? installmentBadge,
        decimal? merchantScore,
        bool isSponsored,
        bool isCheapest,
        int? yearsOnCimri,
        string? offerUrl)
    {
        OfferTitle = Truncate(offerTitle, CimriConsts.MaxOfferTitleLength);
        SellerName = Truncate(sellerName, CimriConsts.MaxSellerNameLength);
        ShippingText = Truncate(shippingText, CimriConsts.MaxOfferTextLength);
        PromotionText = Truncate(promotionText, CimriConsts.MaxOfferTextLength);
        LastUpdatedText = Truncate(lastUpdatedText, CimriConsts.MaxOfferTextLength);
        LastUpdatedUtc = lastUpdatedUtc;
        InstallmentBadge = Truncate(installmentBadge, CimriConsts.MaxBadgeLength);
        MerchantScore = merchantScore;
        IsSponsored = isSponsored;
        IsCheapest = isCheapest;
        YearsOnCimri = yearsOnCimri;
        OfferUrl = Truncate(offerUrl, CimriConsts.MaxOfferUrlLength);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
