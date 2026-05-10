using System;
using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Akakce.Jobs;

[BackgroundJobName("akakce-product-detail")]
public class AkakceProductDetailJobArgs
{
    public Guid ScrapeRunId { get; set; }
    public string ProductCode { get; set; } = default!;
    public string ProductUrl { get; set; } = default!;
    public string? Title { get; set; }
    public string? BrandName { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? BestPriceAmount { get; set; }
    public decimal? PreviousPriceAmount { get; set; }
    public int? OfferCount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public bool IncludeOffers { get; set; } = true;
    public bool ForceRefresh { get; set; }
}
