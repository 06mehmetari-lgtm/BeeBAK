using BeeBAK.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        // Worker host'unda background jobs *yürütmek* zorunlu — IsJobExecutionEnabled true olmalı.
        Configure<AbpBackgroundJobOptions>(options =>
        {
            options.IsJobExecutionEnabled = true;
        });
    }
}
