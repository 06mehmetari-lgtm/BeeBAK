using System.Threading.Tasks;

namespace BeeBAK.Data;

public interface IBeeBAKDbSchemaMigrator
{
    Task MigrateAsync();
}
