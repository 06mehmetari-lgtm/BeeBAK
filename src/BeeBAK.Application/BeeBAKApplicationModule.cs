using System;
using BeeBAK.Marketplaces;
using BeeBAK.Marketplaces.Cimri;
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
        var configuration = context.Services.GetConfiguration();

        context.Services.Configure<CimriClientOptions>(
            configuration.GetSection(CimriClientOptions.SectionName));

        context.Services.AddHttpClient(CimriTelegramListingSyncNotifier.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        context.Services.AddTransient<IListingSyncNotifier, CimriTelegramListingSyncNotifier>();
    }
}
