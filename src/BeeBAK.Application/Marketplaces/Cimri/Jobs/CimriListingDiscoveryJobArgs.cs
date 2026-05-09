using System;
using BeeBAK.Marketplaces.Cimri;
using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

/// <summary>İndirimli ürünler listesini gezip ürün URL'lerini bulup tek tek detail job'larına push eden orkestratör.</summary>
[BackgroundJobName("cimri-listing-discovery")]
public class CimriListingDiscoveryJobArgs
{
    public Guid ScrapeRunId { get; set; }
    public int MaxPages { get; set; } = 5;
    public int MaxProducts { get; set; } = 100;
    public bool IncludeOffers { get; set; } = true;
    public bool ExpandAllOffers { get; set; } = true;
    public bool ForceRefresh { get; set; }

    /// <summary>Selenium ile açılacak tam listeleme URL'si (çoğu zaman senkron sırasında çözümlenmiş).</summary>
    public string? ListingPageUrl { get; set; }

    /// <summary>Null ise çalışma anında <see cref="CimriClientOptions"/> kullanılır.</summary>
    public CimriRetailOfferJobPolicy? RetailOfferPolicy { get; set; }
}
