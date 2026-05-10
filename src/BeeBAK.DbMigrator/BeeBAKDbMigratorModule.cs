using BeeBAK;
using BeeBAK.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace BeeBAK.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(BeeBAKEntityFrameworkCoreModule),
    typeof(BeeBAKApplicationContractsModule),
    typeof(BeeBAKApplicationModule)
)]
public class BeeBAKDbMigratorModule : AbpModule
{
}
