using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Cimri;

public interface ICimriListingSyncAppService : IApplicationService
{
    Task<CimriListingSyncResultDto> SyncAsync(CimriListingSyncInput? input);

    Task<CimriListingSyncStatusDto> GetStatusAsync(Guid scrapeRunId, DateTime? sinceUtc = null);

    Task<CimriListingSyncStatusDto> CancelAsync(Guid scrapeRunId);

    Task<CimriListingSyncStatusDto?> GetLatestAsync();
}
