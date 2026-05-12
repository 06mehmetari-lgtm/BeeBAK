using System;
using BeeBAK.Ecommerce;

namespace BeeBAK.Marketplaces;

/// <summary>
/// Telegram'a başarıyla gönderilmiş bir ürünün özet kaydı.
/// Redis geçmişinden okunur; canlı izleme sayfasında kart olarak gösterilir.
/// </summary>
public class TelegramSentItemDto
{
    /// <summary>Cimri → ContentId, Akakçe → ProductCode</summary>
    public string Id { get; set; } = "";

    public MarketplaceKind Marketplace { get; set; }

    public string Title { get; set; } = "";

    public string? ImageUrl { get; set; }

    public string ProductUrl { get; set; } = "";

    public decimal Price { get; set; }

    public decimal? PreviousPrice { get; set; }

    public decimal? DiscountPercent { get; set; }

    /// <summary>new | price_drop | discount_up</summary>
    public string TriggerType { get; set; } = "new";

    public DateTimeOffset SentAt { get; set; }
}
