using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Akakce;

public interface IAkakceListingSyncAppService : IApplicationService
{
    Task<AkakceListingSyncResultDto> SyncAsync(AkakceListingSyncInput? input);
    Task<AkakceListingSyncStatusDto> GetStatusAsync(Guid scrapeRunId, DateTime? sinceUtc = null);
    Task<AkakceListingSyncStatusDto> CancelAsync(Guid scrapeRunId);
    Task<AkakceListingSyncStatusDto?> GetLatestAsync();
}
