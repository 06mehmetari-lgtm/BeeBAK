using Volo.Abp.Modularity;

namespace BeeBAK;

[DependsOn(
    typeof(BeeBAKApplicationModule),
    typeof(BeeBAKDomainTestModule)
)]
public class BeeBAKApplicationTestModule : AbpModule
{

}
