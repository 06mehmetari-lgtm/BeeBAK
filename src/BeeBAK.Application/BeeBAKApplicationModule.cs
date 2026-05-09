using System;
using System.Net;
using System.Net.Http;
using BeeBAK.Marketplaces;
using BeeBAK.Marketplaces.Cimri;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Volo.Abp.Account;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundJobs.RabbitMQ; // RabbitMQ provider module dependency
using Volo.Abp.Caching;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.DistributedLocking;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;

namespace BeeBAK;

[DependsOn(
    typeof(BeeBAKDomainModule),
    typeof(BeeBAKApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule),
    typeof(AbpBackgroundJobsRabbitMqModule),
    typeof(AbpCachingStackExchangeRedisModule),
    typeof(AbpDistributedLockingModule)
    )]
public class BeeBAKApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        context.Services.Configure<CimriClientOptions>(
            configuration.GetSection(CimriClientOptions.SectionName));

        ConfigureHttpClients(context);
        ConfigureDistributedCache(context, configuration);
        ConfigureDistributedLock(context, configuration);
        ConfigureBackgroundJobs(context, configuration);

        context.Services.AddTransient<IListingSyncNotifier, CimriTelegramListingSyncNotifier>();
    }

    private static void ConfigureHttpClients(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClient(CimriTelegramListingSyncNotifier.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        context.Services
            .AddHttpClient(CimriOfferUrlResolver.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
            });
    }

    private void ConfigureDistributedCache(ServiceConfigurationContext context, IConfiguration configuration)
    {
        Configure<AbpDistributedCacheOptions>(options =>
        {
            options.KeyPrefix = "BeeBAK:";
        });

        var redisConnection = configuration["Redis:Configuration"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            context.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "BeeBAK:";
            });
        }
    }

    private static void ConfigureDistributedLock(ServiceConfigurationContext context, IConfiguration configuration)
    {
        var redisConnection = configuration["Redis:Configuration"];
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            return;
        }

        context.Services.AddSingleton<IDistributedLockProvider>(_ =>
        {
            var multiplexer = ConnectionMultiplexer.Connect(redisConnection);
            return new RedisDistributedSynchronizationProvider(multiplexer.GetDatabase());
        });
    }

    private void ConfigureBackgroundJobs(ServiceConfigurationContext context, IConfiguration configuration)
    {
        var rabbitConnection = configuration["RabbitMq:Connections:Default:HostName"];
        var jobsEnabled = configuration.GetValue<bool?>("BackgroundJobs:IsJobExecutionEnabled") ?? !string.IsNullOrWhiteSpace(rabbitConnection);

        Configure<AbpBackgroundJobOptions>(options =>
        {
            options.IsJobExecutionEnabled = jobsEnabled;
        });
    }
}
