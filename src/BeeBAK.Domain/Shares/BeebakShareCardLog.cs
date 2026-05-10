using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace BeeBAK.Shares;

/// <summary>Paylaşılan kartın denetim kaydı (ne zaman, hangi ürün kümesi).</summary>
public class BeebakShareCardLog : Entity<Guid>
{
    public DateTime CreatedUtc { get; protected set; }

    public string ChannelName { get; protected set; } = BeebakShareConsts.DefaultChannelName;

    /// <summary>Kart görünümü için JSON snapshot.</summary>
    public string CardPayloadJson { get; protected set; } = "{}";

    /// <summary>Sıralı içerik kimlikleri — tekrar kontrolü.</summary>
    public string ProductFingerprint { get; protected set; } = "";

    protected BeebakShareCardLog()
    {
    }

    public BeebakShareCardLog(
        Guid id,
        DateTime createdUtc,
        string channelName,
        string cardPayloadJson,
        string productFingerprint)
        : base(id)
    {
        CreatedUtc = createdUtc;
        ChannelName = Check.NotNullOrWhiteSpace(channelName, nameof(channelName), maxLength: 64);
        CardPayloadJson = Check.NotNull(cardPayloadJson, nameof(cardPayloadJson));
        ProductFingerprint = Check.NotNullOrWhiteSpace(productFingerprint, nameof(productFingerprint), maxLength: BeebakShareConsts.MaxFingerprintLength);
    }
}
