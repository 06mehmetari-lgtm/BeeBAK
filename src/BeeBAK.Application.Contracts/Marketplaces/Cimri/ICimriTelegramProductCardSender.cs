using System.Threading;
using System.Threading.Tasks;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Cimri ürünü kaydedildikten sonra Telegram kanalına kart benzeri özet (görsel + metin) gönderir.</summary>
public interface ICimriTelegramProductCardSender
{
    /// <summary>
    /// Ürünü Telegram'a gönderir.
    /// <paramref name="triggerType"/>: "new" | "price_drop" | "discount_up"
    /// </summary>
    Task TrySendAfterProductIngestedAsync(
        string contentId,
        string triggerType = "new",
        CancellationToken cancellationToken = default);
}
