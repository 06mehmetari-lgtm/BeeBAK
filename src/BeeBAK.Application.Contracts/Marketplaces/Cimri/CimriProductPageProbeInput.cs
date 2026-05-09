using System.ComponentModel.DataAnnotations;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriProductPageProbeInput
{
    /// <summary>Tam Cimri ürün detay URL'si (https://www.cimri.com/.../...,{contentId}).</summary>
    [Required]
    [StringLength(2048)]
    public string ProductUrl { get; set; } = default!;

    /// <summary>"+9 Fiyat Teklifini Gör" tıklanıp tüm teklifler genişletilsin mi.</summary>
    public bool ExpandAllOffers { get; set; } = true;

    /// <summary>true ise sonuç DB'ye yazılır; false ise yalnızca scrape sonucu döner.</summary>
    public bool Persist { get; set; } = true;
}
