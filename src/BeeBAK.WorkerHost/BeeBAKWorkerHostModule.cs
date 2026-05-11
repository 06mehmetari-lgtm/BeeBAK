using System.Threading.Tasks;
using BeeBAK.EntityFrameworkCore;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Marketplaces.Cimri.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
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

        context.Services.AddTransient<CimriAutoSyncWorker>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var options = context.ServiceProvider
            .GetRequiredService<IOptions<CimriClientOptions>>().Value;

        var logger = context.ServiceProvider
            .GetRequiredService<ILogger<BeeBAKWorkerHostModule>>();

        logger.LogInformation(
            "CimriAutoSync: Enabled={Enabled}, IntervalHours={Interval}, RunOnStart={RunOnStart}, MaxPages={Pages}, MaxProducts={Products}",
            options.AutoSync.Enabled,
            options.AutoSync.IntervalHours,
            options.AutoSync.RunOnStart,
            options.AutoSync.MaxPages,
            options.AutoSync.MaxProducts);

        await context.ServiceProvider
            .GetRequiredService<IBackgroundWorkerManager>()
            .AddAsync(context.ServiceProvider.GetRequiredService<CimriAutoSyncWorker>());
    }
}
