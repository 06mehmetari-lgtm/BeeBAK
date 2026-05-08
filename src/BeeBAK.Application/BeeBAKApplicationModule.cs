using System;
using BeeBAK.Marketplaces;
using BeeBAK.Marketplaces.Trendyol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.Account;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Mapperly;
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
    typeof(AbpSettingManagementApplicationModule)
    )]
public class BeeBAKApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Configure<TrendyolClientOptions>(
            context.Configuration.GetSection(TrendyolClientOptions.SectionName));

        context.Services.AddHttpClient(TrendyolSearchHttpClient.HttpClientName, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptionsMonitor<TrendyolClientOptions>>().CurrentValue;
            var seconds = opts.RequestTimeoutSeconds <= 0 ? 60 : opts.RequestTimeoutSeconds;
            client.Timeout = TimeSpan.FromSeconds(seconds);
        });

        context.Services.AddHttpClient(TelegramListingSyncNotifier.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        context.Services.AddTransient<IListingSyncNotifier, TelegramListingSyncNotifier>();
    }
}
