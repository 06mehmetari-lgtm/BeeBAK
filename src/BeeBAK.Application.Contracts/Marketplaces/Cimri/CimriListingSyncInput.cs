using System.Text.Json.Serialization;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriListingSyncInput
{
    /// <summary>İndirimli ürünler listesinde okunacak en fazla sayfa sayısı (Daha Fazla Ürün Göster ile sayfalama).</summary>
    [JsonPropertyName("maxPages")]
    public int? MaxPages { get; set; }

    /// <summary>Veritabanına yazılacak ürün üst sınırı (sayfalama erken biter).</summary>
    [JsonPropertyName("maxProducts")]
    public int? MaxProducts { get; set; }

    /// <summary>Her ürün için detay sayfasına gidip teklifleri çekilsin mi (yavaş ama tam veri).</summary>
    [JsonPropertyName("includeOffers")]
    public bool IncludeOffers { get; set; } = true;

    /// <summary>"+9 Fiyat Teklifini Gör" butonuna tıklayıp tüm teklifleri açsın mı (sadece IncludeOffers=true ile etkili).</summary>
    [JsonPropertyName("expandAllOffers")]
    public bool ExpandAllOffers { get; set; } = true;

    [JsonPropertyName("forceRefresh")]
    public bool ForceRefresh { get; set; }
}
