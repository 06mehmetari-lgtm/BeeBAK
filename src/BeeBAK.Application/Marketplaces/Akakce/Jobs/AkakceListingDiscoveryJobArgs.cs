using System;
using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Akakce.Jobs;

[BackgroundJobName("akakce-listing-discovery")]
public class AkakceListingDiscoveryJobArgs
{
    public Guid ScrapeRunId { get; set; }
    public int MaxPages { get; set; } = 5;
    public int MaxProducts { get; set; } = 100;
    public bool IncludeOffers { get; set; } = true;
    public bool ForceRefresh { get; set; }
    public string? ListingPageUrl { get; set; }
}
