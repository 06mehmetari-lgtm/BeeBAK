using Volo.Abp.Application.Dtos;

namespace BeeBAK.Marketplaces.Akakce;

public class GetAkakceProductListInput : PagedAndSortedResultRequestDto
{
    public string? Search { get; set; }
    public bool IncludeOffers { get; set; }
}
