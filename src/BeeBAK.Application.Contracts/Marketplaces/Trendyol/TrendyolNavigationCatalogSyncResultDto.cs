namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolNavigationCatalogSyncResultDto
{
    public int CategoriesCreated { get; set; }

    public int CategoriesUpdated { get; set; }

    /// <summary>Takip listesinde olup HTML içinde eşleşme bulunamayan kodlar.</summary>
    public int TrackersMissingInHtml { get; set; }

    public int SectionAnchorsParsed { get; set; }
}
