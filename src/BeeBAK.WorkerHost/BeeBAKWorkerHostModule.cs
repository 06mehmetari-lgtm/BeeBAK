using System.Threading.Tasks;
using BeeBAK.EntityFrameworkCore;
using BeeBAK.Marketplaces.Akakce;
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

        // Robot 1 — Otomatik tarama (kategori rotasyonlu)
        context.Services.AddTransient<CimriAutoSyncWorker>();
        context.Services.AddTransient<AkakceAutoSyncWorker>();

        // Telegram yayın motorları
        context.Services.AddTransient<CimriTelegramPublisherWorker>();
        context.Services.AddTransient<AkakceTelegramPublisherWorker>();

        // Robot 2 — Fiyat güncelleme robotları
        context.Services.AddTransient<CimriPriceWatchWorker>();
        context.Services.AddTransient<AkakcePriceWatchWorker>();

        // Robot 3 — Sistem sağlığı izleme
        context.Services.AddTransient<SystemHealthWorker>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var cimriOpts  = context.ServiceProvider.GetRequiredService<IOptions<CimriClientOptions>>().Value;
        var akakceOpts = context.ServiceProvider.GetRequiredService<IOptions<AkakceClientOptions>>().Value;
        var logger     = context.ServiceProvider.GetRequiredService<ILogger<BeeBAKWorkerHostModule>>();

        logger.LogInformation(
            "CimriAutoSync: Enabled={Enabled}, IntervalHours={Interval}, CategoryCount={CatCount}, RunOnStart={RunOnStart}, MaxPages={Pages}, MaxProducts={Products}",
            cimriOpts.AutoSync.Enabled, cimriOpts.AutoSync.IntervalHours,
            cimriOpts.AutoSync.CategoryUrls.Count, cimriOpts.AutoSync.RunOnStart,
            cimriOpts.AutoSync.MaxPages, cimriOpts.AutoSync.MaxProducts);

        logger.LogInformation(
            "CimriPriceWatch: Enabled={Enabled}, IntervalMinutes={Interval}, ProductAgeDays={AgeDays}",
            cimriOpts.PriceWatch.Enabled, cimriOpts.PriceWatch.IntervalMinutes, cimriOpts.PriceWatch.ProductAgeDays);

        logger.LogInformation(
            "AkakceAutoSync: Enabled={Enabled}, IntervalHours={Interval}, CategoryCount={CatCount}, RunOnStart={RunOnStart}, MaxPages={Pages}, MaxProducts={Products}",
            akakceOpts.AutoSync.Enabled, akakceOpts.AutoSync.IntervalHours,
            akakceOpts.AutoSync.CategoryUrls.Count, akakceOpts.AutoSync.RunOnStart,
            akakceOpts.AutoSync.MaxPages, akakceOpts.AutoSync.MaxProducts);

        logger.LogInformation(
            "AkakcePriceWatch: Enabled={Enabled}, IntervalMinutes={Interval}, ProductAgeDays={AgeDays}",
            akakceOpts.PriceWatch.Enabled, akakceOpts.PriceWatch.IntervalMinutes, akakceOpts.PriceWatch.ProductAgeDays);

        var workerManager = context.ServiceProvider.GetRequiredService<IBackgroundWorkerManager>();

        // Robot 3 önce başlar (health monitoring ilk aktif)
        await workerManager.AddAsync(context.ServiceProvider.GetRequiredService<SystemHealthWorker>());

        // Robot 1 — Tarama
        await workerManager.AddAsync(context.ServiceProvider.GetRequiredService<CimriAutoSyncWorker>());
        await workerManager.AddAsync(context.ServiceProvider.GetRequiredService<AkakceAutoSyncWorker>());

        // Telegram yayın
        await workerManager.AddAsync(context.ServiceProvider.GetRequiredService<CimriTelegramPublisherWorker>());
        await workerManager.AddAsync(context.ServiceProvider.GetRequiredService<AkakceTelegramPublisherWorker>());

        // Robot 2 — Fiyat takibi (AutoSync'ten biraz geç başlar)
        await workerManager.AddAsync(context.ServiceProvider.GetRequiredService<CimriPriceWatchWorker>());
        await workerManager.AddAsync(context.ServiceProvider.GetRequiredService<AkakcePriceWatchWorker>());
    }
}
