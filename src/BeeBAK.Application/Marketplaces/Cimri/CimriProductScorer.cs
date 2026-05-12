namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Ürünün Telegram yayın öncelik skorunu hesaplar.
/// Yüksek skor → kuyruktan önce çekilir.
/// </summary>
public static class CimriProductScorer
{
    /// <summary>
    /// Skoru hesapla.
    /// max ~150 puan; güncelleme + yüksek indirim kombinasyonu en yüksek değeri alır.
    /// </summary>
    public static double Calculate(decimal? discountPercent, string triggerType)
    {
        double score = 0d;

        // İndirim oranı puanı
        if (discountPercent >= 50m)      score += 100;
        else if (discountPercent >= 25m) score += 70;
        else if (discountPercent >= 10m) score += 40;
        else                             score += 10;

        // Tetikleyici türü bonusu
        score += triggerType switch
        {
            "price_drop"    => 30,
            "discount_up"   => 25,
            "new"           => 0,
            _               => 0,
        };

        return score;
    }

    /// <summary>
    /// Ürünün fingerprint'ini hesaplar: fiyat veya indirim değişirse yeniden gönderilebilir.
    /// </summary>
    public static string BuildFingerprint(decimal lowestPrice, decimal? discountPercent)
    {
        var p = (int)(lowestPrice);                   // tam sayı TL hassasiyeti yeterli
        var d = (int)(discountPercent ?? 0m);
        return $"{p}_{d}";
    }
}
