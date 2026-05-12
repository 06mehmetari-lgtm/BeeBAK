using System.Threading;
using System.Threading.Tasks;

namespace BeeBAK.Marketplaces.Akakce;

public interface IAkakceTelegramProductCardSender
{
    Task TrySendAfterProductIngestedAsync(
        string productCode,
        string triggerType = "new",
        CancellationToken cancellationToken = default);
}
