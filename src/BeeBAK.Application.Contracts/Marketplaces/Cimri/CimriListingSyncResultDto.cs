using System;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriListingSyncResultDto
{
    public Guid ScrapeRunId { get; set; }

    public int PagesFetched { get; set; }

    public int ProductsAffected { get; set; }

    public int OffersAffected { get; set; }

    public int MerchantsAffected { get; set; }

    public string ResolvedListingPageUrl { get; set; } = default!;

    /// <summary>true ise iş queue'ya iletildi; sayım metrikleri worker tamamlandıktan sonra DB'den okunur.</summary>
    public bool Queued { get; set; }
}
