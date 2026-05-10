using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace BeeBAK.Shares;

/// <summary>Aynı ürünün aynı gün içinde ikinci kez paylaşım kartında yer almasını engeller.</summary>
public class BeebakShareProductDayBlock : Entity<Guid>
{
    public string CimriContentId { get; protected set; } = default!;

    /// <summary>UTC takvim günü (00:00).</summary>
    public DateTime BlockUtcDate { get; protected set; }

    public string ChannelName { get; protected set; } = BeebakShareConsts.DefaultChannelName;

    public DateTime CreatedUtc { get; protected set; }

    protected BeebakShareProductDayBlock()
    {
    }

    public BeebakShareProductDayBlock(
        Guid id,
        string cimriContentId,
        DateTime blockUtcDate,
        string channelName,
        DateTime createdUtc)
        : base(id)
    {
        CimriContentId = Check.NotNullOrWhiteSpace(cimriContentId, nameof(cimriContentId), maxLength: BeebakShareConsts.MaxCimriContentIdLength);
        BlockUtcDate = blockUtcDate.Date;
        ChannelName = Check.NotNullOrWhiteSpace(channelName, nameof(channelName), maxLength: 64);
        CreatedUtc = createdUtc;
    }
}
