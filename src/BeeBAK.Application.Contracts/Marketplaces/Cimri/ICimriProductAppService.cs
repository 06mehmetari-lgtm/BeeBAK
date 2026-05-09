using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Cimri;

public interface ICimriProductAppService : IApplicationService
{
    Task<CimriProductDto> GetAsync(Guid id);

    Task<PagedResultDto<CimriProductDto>> GetListAsync(GetCimriProductListInput input);
}
