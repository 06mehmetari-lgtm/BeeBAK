using System.Collections.Generic;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri ürün detay sayfasının Selenium ile alınmış sonucu: rendered HTML ve teklif kartı tıklamalarından
/// hasat edilmiş redirect URL listesi (sıralı; offer card sırasıyla aynı index).
/// </summary>
public sealed class CimriProductDetailFetchResult
{
    public string? Html { get; set; }

    public IReadOnlyList<string> CapturedOfferUrls { get; set; } = new List<string>();
}
