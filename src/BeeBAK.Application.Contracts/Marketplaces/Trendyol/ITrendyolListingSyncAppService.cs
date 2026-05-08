using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Trendyol;

public interface ITrendyolListingSyncAppService : IApplicationService
{
    Task<TrendyolListingSyncResultDto> SyncAsync(TrendyolListingSyncInput input);
}
