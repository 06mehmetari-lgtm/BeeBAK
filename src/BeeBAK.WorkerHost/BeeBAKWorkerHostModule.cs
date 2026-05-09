using BeeBAK.EntityFrameworkCore;
using BeeBAK.Marketplaces.Cimri.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Modularity;

namespace BeeBAK;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(BeeBAKApplicationModule),
    typeof(BeeBAKEntityFrameworkCoreModule)
    )]
public class BeeBAKWorkerHostModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpBackgroundJobOptions>(options =>
        {
            options.IsJobExecutionEnabled = true;
        });

        context.Services.Replace(
            ServiceDescriptor.Transient<IBackgroundJobExecuter, LoggingBackgroundJobExecuter>());
    }
}
