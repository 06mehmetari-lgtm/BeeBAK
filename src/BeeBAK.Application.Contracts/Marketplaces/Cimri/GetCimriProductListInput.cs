using Volo.Abp.Application.Dtos;

namespace BeeBAK.Marketplaces.Cimri;

public class GetCimriProductListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Başlık veya marka filtresi (ILIKE).</summary>
    public string? Search { get; set; }

    /// <summary>true ise her ürünle birlikte teklif listesi de döndürülür (büyük yanıt).</summary>
    public bool IncludeOffers { get; set; }
}
