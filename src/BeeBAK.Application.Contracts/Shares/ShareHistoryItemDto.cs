using System;

namespace BeeBAK.Shares;

public class ShareHistoryItemDto
{
    public Guid Id { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string ChannelName { get; set; } = "";

    public string ProductFingerprint { get; set; } = "";

    /// <summary>Kısa özet (ilk ürün başlığı vb.).</summary>
    public string? SummaryLine { get; set; }
}
