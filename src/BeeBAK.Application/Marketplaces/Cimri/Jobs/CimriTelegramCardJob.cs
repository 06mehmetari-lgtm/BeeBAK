using System.Threading.Tasks;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

public class CimriTelegramCardJob : AsyncBackgroundJob<CimriTelegramCardJobArgs>, ITransientDependency
{
    private readonly ICimriTelegramProductCardSender _sender;

    public CimriTelegramCardJob(ICimriTelegramProductCardSender sender)
    {
        _sender = sender;
    }

    public override Task ExecuteAsync(CimriTelegramCardJobArgs args)
    {
        return _sender.TrySendAfterProductIngestedAsync(args.ContentId);
    }
}
