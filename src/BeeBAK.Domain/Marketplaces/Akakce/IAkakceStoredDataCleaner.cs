using System.Threading;
using System.Threading.Tasks;

namespace BeeBAK.Marketplaces.Akakce;

public interface IAkakceStoredDataCleaner
{
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
