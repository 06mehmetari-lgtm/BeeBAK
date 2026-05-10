using System.Text.Json.Serialization;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceListingSyncInput
{
    [JsonPropertyName("maxPages")]
    public int? MaxPages { get; set; }

    [JsonPropertyName("maxProducts")]
    public int? MaxProducts { get; set; }

    [JsonPropertyName("includeOffers")]
    public bool IncludeOffers { get; set; } = true;

    [JsonPropertyName("includeProductDetails")]
    public bool? IncludeProductDetails { get; set; }

    [JsonPropertyName("forceRefresh")]
    public bool ForceRefresh { get; set; }

    [JsonPropertyName("listingPageUrl")]
    public string? ListingPageUrl { get; set; }
}
