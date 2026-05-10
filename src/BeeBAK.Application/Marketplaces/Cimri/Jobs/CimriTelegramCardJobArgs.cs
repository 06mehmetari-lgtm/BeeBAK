using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

[BackgroundJobName("cimri-telegram-card")]
public class CimriTelegramCardJobArgs
{
    public string ContentId { get; set; } = default!;
}
