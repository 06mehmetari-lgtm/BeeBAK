using Volo.Abp.Modularity;

namespace BeeBAK;

public abstract class BeeBAKApplicationTestBase<TStartupModule> : BeeBAKTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
