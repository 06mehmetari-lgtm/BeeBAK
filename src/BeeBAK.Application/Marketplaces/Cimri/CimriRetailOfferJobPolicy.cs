using System.Collections.Generic;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Background job args ile taşınan perakende teklif filtresi (JSON-serialize).
/// </summary>
public class CimriRetailOfferJobPolicy
{
    public bool RestrictToAllowedMerchants { get; set; }

    public bool RequireMerchantProductId { get; set; }

    public bool SkipProductIfNoQualifiedOffers { get; set; }

    public List<string>? AllowedMerchantSubstrings { get; set; }
}
