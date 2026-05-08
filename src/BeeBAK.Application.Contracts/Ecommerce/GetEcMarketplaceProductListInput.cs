using System.ComponentModel.DataAnnotations;
using BeeBAK.Marketplaces;
using Volo.Abp.Application.Dtos;

namespace BeeBAK.Ecommerce;

public class GetEcMarketplaceProductListInput : PagedResultRequestDto
{
    [Required]
    public MarketplaceKind Marketplace { get; set; }
}
