using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Akakce;

public interface IAkakceProductAppService : IApplicationService
{
    Task<AkakceProductDto> GetAsync(Guid id);
    Task<PagedResultDto<AkakceProductDto>> GetListAsync(GetAkakceProductListInput input);
    Task ClearAllStoredDataAsync();
}
