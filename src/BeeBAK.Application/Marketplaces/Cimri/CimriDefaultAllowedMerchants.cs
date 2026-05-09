namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Büyük perakendeciler — mağaza adında alt-dize eşleşmesi (kültürden bağımsız küçük harf).</summary>
public static class CimriDefaultAllowedMerchants
{
    /// <summary>
    /// Varsayılan anahtar kelimeler (Trendyol, Hepsiburada, Amazon, N11, ÇiçekSepeti, Pazarama ve yaygın yazımlar).
    /// </summary>
    public static readonly string[] Substrings =
    {
        "trendyol",
        "hepsiburada",
        "hepsibuda",
        "amazon",
        "n11",
        "çiçeksepeti",
        "ciceksepeti",
        "pazarama",
    };
}
