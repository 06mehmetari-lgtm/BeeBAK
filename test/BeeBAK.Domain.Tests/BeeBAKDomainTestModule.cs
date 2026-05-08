using Volo.Abp.Modularity;

namespace BeeBAK;

[DependsOn(
    typeof(BeeBAKDomainModule),
    typeof(BeeBAKTestBaseModule)
)]
public class BeeBAKDomainTestModule : AbpModule
{

}
