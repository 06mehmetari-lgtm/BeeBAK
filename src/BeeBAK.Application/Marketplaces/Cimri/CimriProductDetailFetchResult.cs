using System;
using System.Collections.Generic;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri ürün detay sayfasının Selenium ile alınmış sonucu: rendered HTML ve teklif kartı tıklamalarından
/// hasat edilmiş redirect URL listesi (sıralı; offer card sırasıyla aynı index).
/// </summary>
public sealed class CimriProductDetailFetchResult
{
    public string? Html { get; set; }

    /// <summary>
    /// Teklif kartı tıklama akışından yakalanmış URL listesi. Çoğunlukla
    /// <c>https://www.cimri.com/offer/{id}?...</c> biçiminde Cimri redirect URL'leri içerir.
    /// </summary>
    public IReadOnlyList<string> CapturedOfferUrls { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Aynı Selenium oturumunda <c>/offer/{id}</c> redirect URL'lerinin yeni tab'da takip edilerek
    /// çözülmüş asıl mağaza URL'leri. Key = orijinal Cimri redirect URL, Value = nihai mağaza URL.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolvedMerchantUrls { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
