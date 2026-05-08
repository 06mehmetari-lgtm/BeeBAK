using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace BeeBAK.Ecommerce;

public interface IEcMarketplaceProductAppService : IApplicationService
{
    Task<PagedResultDto<EcMarketplaceProductDto>> GetListAsync(GetEcMarketplaceProductListInput input);
}
