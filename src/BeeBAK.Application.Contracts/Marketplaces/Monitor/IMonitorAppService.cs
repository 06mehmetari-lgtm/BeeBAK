using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Monitor;

public interface IMonitorAppService : IApplicationService
{
    /// <summary>
    /// Tek API çağrısıyla tüm aktif tarama çalışmaları + son Telegram paylaşımları.
    /// </summary>
    Task<AllActiveRunsDto> GetAllActiveAsync();
}
