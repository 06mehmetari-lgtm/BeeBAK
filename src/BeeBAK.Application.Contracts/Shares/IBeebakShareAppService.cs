using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace BeeBAK.Shares;

public interface IBeebakShareAppService : IApplicationService
{
    Task<BeebakShareDeckDto> BuildDeckAsync(ShareDeckBuildInput input);

    Task RecordShareAsync(RecordShareInput input);

    Task<PagedResultDto<ShareHistoryItemDto>> GetHistoryAsync(PagedAndSortedResultRequestDto input);
}
