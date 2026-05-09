using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Cimri;

public interface ICimriProductPageProbeAppService : IApplicationService
{
    Task<CimriProductPageProbeResultDto> ProbeProductPageAsync(CimriProductPageProbeInput input);
}
