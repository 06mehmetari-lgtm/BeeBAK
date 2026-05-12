using System;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakcePublishQueueEntry
{
    public string ProductCode { get; set; } = "";
    public string TriggerType { get; set; } = "new";
    public double Score { get; set; }
    public decimal LowestPrice { get; set; }
    public decimal? PreviousPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
    public long EnqueuedAtUnix { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
