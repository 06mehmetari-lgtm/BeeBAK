using System;
using BeeBAK.Marketplaces.Cimri;
using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

/// <summary>Tek bir Cimri ürün detay sayfasını çekip DB'ye yazan, queue'da tek iş = tek ürün job'u.</summary>
[BackgroundJobName("cimri-product-detail")]
public class CimriProductDetailJobArgs
{
    public Guid ScrapeRunId { get; set; }
    public string ContentId { get; set; } = default!;
    public string ProductUrl { get; set; } = default!;

    public string? CategorySlug { get; set; }
    public string? Title { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? BestPriceAmount { get; set; }
    public string? BestMerchantName { get; set; }
    public decimal? PreviousPriceAmount { get; set; }
    public int? OfferCount { get; set; }
    public decimal? DiscountPercent { get; set; }

    public bool ExpandAllOffers { get; set; } = true;
    public bool IncludeOffers { get; set; } = true;
    public bool ForceRefresh { get; set; }

    /// <summary>Null ise çalışma anında <see cref="CimriClientOptions"/> kullanılır.</summary>
    public CimriRetailOfferJobPolicy? RetailOfferPolicy { get; set; }
}
