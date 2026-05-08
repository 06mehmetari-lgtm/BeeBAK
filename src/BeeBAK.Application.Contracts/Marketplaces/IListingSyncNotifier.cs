using System.Threading;
using System.Threading.Tasks;

namespace BeeBAK.Marketplaces;

public interface IListingSyncNotifier
{
    Task NotifyListingSyncCompletedAsync(
        ListingSyncNotificationContext context,
        CancellationToken cancellationToken = default);
}
