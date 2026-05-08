using BeeBAK.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace BeeBAK.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(BeeBAKEntityFrameworkCoreModule),
    typeof(BeeBAKApplicationContractsModule)
)]
public class BeeBAKDbMigratorModule : AbpModule
{
}
