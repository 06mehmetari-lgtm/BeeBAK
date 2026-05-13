using System;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Telegram yayın kuyruğundaki bir ürün kaydı.
/// Trigger türüne ve indirim oranına göre hesaplanmış öncelik skoru taşır.
/// </summary>
public class CimriPublishQueueEntry
{
    public string ContentId { get; set; } = "";

    /// <summary>Ürün başlığı — kategori filtresi için (publisher worker'da kullanılır)</summary>
    public string? Title { get; set; }

    /// <summary>new | price_drop | discount_up</summary>
    public string TriggerType { get; set; } = "new";

    public double Score { get; set; }

    public decimal LowestPrice { get; set; }

    public decimal? PreviousPrice { get; set; }

    public decimal? DiscountPercent { get; set; }

    /// <summary>En ucuz teklifin mağaza adı — çeşitlilik kontrolü için</summary>
    public string MerchantName { get; set; } = "";

    /// <summary>Ürün kategori slug'ı — kategori bazlı filtreler için</summary>
    public string? CategorySlug { get; set; }

    public long EnqueuedAtUnix { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
