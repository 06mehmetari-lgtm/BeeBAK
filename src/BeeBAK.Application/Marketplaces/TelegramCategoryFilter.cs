using System;

namespace BeeBAK.Marketplaces;

/// <summary>
/// Telegram paylaşımından kaçınılacak kategori ve başlık kuralları.
/// Hem ingestion sırasında (kuyruğa girmeden önce) hem de
/// publisher worker'da (kuyruktan çıktıktan sonra) kontrol edilir.
/// </summary>
public static class TelegramCategoryFilter
{
    // ── Kategori slug / path içinde geçmemesi gereken anahtar kelimeler ──
    // ÖNEMLİ: "kitap" tek başına KULLANILMAZ çünkü Cimri'de "Kitap, Müzik, Hobi"
    // üst kategorisi müzik, oyun ve hobi ürünlerini de içerir; "kitap" bunu da eşleştirip
    // tüm bu kategorileri yanlışlıkla engeller. Bunun yerine "kitaplar" (gerçek kitap alt
    // kategorisi) ve diğer özgün keyword'ler kullanılır.
    private static readonly string[] BlockedCategoryKeywords =
    [
        // ── Kitap alt kategorileri ──
        "kitaplar",          // Cimri: "› Kitaplar ›" — gerçek kitap alt kategorisi
        "book",
        "books",
        "roman",
        "edebiyat",
        "e-kitap",
        "ekitap",
        "hikaye",
        "polisiye",
        "bilim-kurgu",
        "fantastik",
        "ders-kitap",
        "cocuk-kitap",
        "yayinlari",         // yayınları — yayınevine ait kategori
        "yayinevi",
        // ── Basın / medya ──
        "dergi",
        "magazine",
        "gazete",
        "newspaper",
        // ── Referans ──
        "sozluk",
        "ansiklopedi",
        "atlas",
        "masal",
    ];

    // ── Başlıkta tam kelime olarak geçmemesi gereken ifadeler ──
    private static readonly string[] BlockedTitleKeywords =
    [
        "kitap",
        "kitabi",       // kitabı
        "kitabin",      // kitabın
        "kitabini",     // kitabını
        "kitapligi",    // kitaplığı
        "roman",
        "hikaye kitabi",
        "ders kitabi",
        "egitim seti",
        "sozluk",
        "ansiklopedi",
        "polisiye",
    ];

    /// <summary>
    /// Ürünün Telegram'a gönderilmesi engelleniyorsa true döner.
    /// </summary>
    /// <param name="categorySlug">Kategori slug veya tam kategori yolu (null güvenli)</param>
    /// <param name="title">Ürün başlığı (null güvenli)</param>
    public static bool IsBlocked(string? categorySlug, string? title)
    {
        // ── Kategori kontrolü ─────────────────────────────────────────────
        if (!string.IsNullOrEmpty(categorySlug))
        {
            var slug = categorySlug.ToLowerInvariant()
                .Replace('ı', 'i').Replace('ğ', 'g').Replace('ş', 's')
                .Replace('ç', 'c').Replace('ö', 'o').Replace('ü', 'u');

            foreach (var kw in BlockedCategoryKeywords)
            {
                if (slug.Contains(kw)) return true;
            }
        }

        // ── Başlık kontrolü (tam kelime) ──────────────────────────────────
        if (!string.IsNullOrEmpty(title))
        {
            var t = title.ToLowerInvariant()
                .Replace('ı', 'i').Replace('ğ', 'g').Replace('ş', 's')
                .Replace('ç', 'c').Replace('ö', 'o').Replace('ü', 'u');

            foreach (var kw in BlockedTitleKeywords)
            {
                // Tam kelime eşleşmesi: başında/ortasında/sonunda boşluk veya satır başı/sonu
                var idx = t.IndexOf(kw, StringComparison.Ordinal);
                while (idx >= 0)
                {
                    var before = idx == 0 || !char.IsLetterOrDigit(t[idx - 1]);
                    var after  = idx + kw.Length >= t.Length || !char.IsLetterOrDigit(t[idx + kw.Length]);
                    if (before && after) return true;
                    idx = t.IndexOf(kw, idx + 1, StringComparison.Ordinal);
                }
            }
        }

        return false;
    }
}
