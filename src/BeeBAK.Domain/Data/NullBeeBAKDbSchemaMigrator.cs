using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Data;

/* This is used if database provider does't define
 * IBeeBAKDbSchemaMigrator implementation.
 */
public class NullBeeBAKDbSchemaMigrator : IBeeBAKDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
