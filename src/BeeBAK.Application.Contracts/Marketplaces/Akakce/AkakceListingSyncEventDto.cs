using System;
using BeeBAK.Ecommerce;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceListingSyncEventDto
{
    public Guid Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public EcScrapeRunEventLevel Level { get; set; }
    public string Phase { get; set; } = default!;
    public string Message { get; set; } = default!;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public int? Index { get; set; }
    public int? Total { get; set; }
}
