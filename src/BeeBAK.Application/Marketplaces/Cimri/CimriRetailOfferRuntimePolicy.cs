using System.Collections.Generic;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Tek bir senkron veya job çalışması için mağaza/teklif kurallarının snapshot'ı.
/// </summary>
public sealed record CimriRetailOfferRuntimePolicy(
    bool RestrictToAllowedMerchants,
    IReadOnlyList<string> AllowedMerchantSubstrings,
    bool RequireMerchantProductId,
    bool SkipProductIfNoQualifiedOffers);
