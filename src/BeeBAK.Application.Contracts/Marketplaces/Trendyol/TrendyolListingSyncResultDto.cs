using System;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolListingSyncResultDto
{
    public Guid ScrapeRunId { get; set; }

    public int PagesFetched { get; set; }

    public int ProductsAffected { get; set; }

    public string ResolvedSearchQuery { get; set; } = default!;
}
