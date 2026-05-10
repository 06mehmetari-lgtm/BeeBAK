using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Cimri;

public interface ICimriProductAppService : IApplicationService
{
    Task<CimriProductDto> GetAsync(Guid id);

    Task<PagedResultDto<CimriProductDto>> GetListAsync(GetCimriProductListInput input);

    /// <summary>Cimri ürünleri, teklifler, mağazalar ve ilgili senkron/paylaşım kayıtlarını siler.</summary>
    Task ClearAllStoredDataAsync();
}
