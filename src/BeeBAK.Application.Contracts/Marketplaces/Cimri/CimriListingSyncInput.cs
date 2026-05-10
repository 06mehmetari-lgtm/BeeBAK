using System.Collections.Generic;
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

    /// <summary>Selenium ile açılacak tam HTTPS listeleme URL'si. Boşsa yalnızca Cimri:ListingPageUrl (BaseUrl+yol birleştirilmez).</summary>
    [JsonPropertyName("listingPageUrl")]
    public string? ListingPageUrl { get; set; }

    /// <summary>UI uyumu: Angular <c>includeProductDetails</c> ile aynı anlamda.</summary>
    [JsonPropertyName("includeProductDetails")]
    public bool? IncludeProductDetails { get; set; }

    /// <summary>Yalnızca izin verilen mağaza adları (alt-dize) ile eşleşen teklifleri kaydet.</summary>
    [JsonPropertyName("restrictOffersToAllowedMerchants")]
    public bool? RestrictOffersToAllowedMerchants { get; set; }

    /// <summary>Kayıtlı tekliflerde mağaza ürün kimliği zorunlu.</summary>
    [JsonPropertyName("requireMerchantProductId")]
    public bool? RequireMerchantProductId { get; set; }

    /// <summary>Uygun teklif yoksa ürün oluşturma / var olanı sil.</summary>
    [JsonPropertyName("skipProductWithoutQualifiedOffers")]
    public bool? SkipProductWithoutQualifiedOffers { get; set; }

    /// <summary>Boş veya null ise yapılandırma varsayılanı.</summary>
    [JsonPropertyName("allowedMerchantSubstrings")]
    public List<string>? AllowedMerchantSubstrings { get; set; }
}
