using System.Threading;
using System.Threading.Tasks;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Cimri ürünü kaydedildikten sonra Telegram kanalına kart benzeri özet (görsel + metin) gönderir.</summary>
public interface ICimriTelegramProductCardSender
{
    Task TrySendAfterProductIngestedAsync(string contentId, CancellationToken cancellationToken = default);
}
