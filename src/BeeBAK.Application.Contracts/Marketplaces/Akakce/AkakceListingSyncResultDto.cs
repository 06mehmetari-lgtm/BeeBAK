using System;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceListingSyncResultDto
{
    public Guid ScrapeRunId { get; set; }
    public int PagesFetched { get; set; }
    public int ProductsAffected { get; set; }
    public int OffersAffected { get; set; }
    public int MerchantsAffected { get; set; }
    public string? ResolvedListingPageUrl { get; set; }
    public bool Queued { get; set; }
}
