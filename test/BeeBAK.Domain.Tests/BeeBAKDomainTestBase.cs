using Volo.Abp.Modularity;

namespace BeeBAK;

/* Inherit from this class for your domain layer tests. */
public abstract class BeeBAKDomainTestBase<TStartupModule> : BeeBAKTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
