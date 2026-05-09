using System;
using System.Collections.Generic;
using System.Linq;

namespace BeeBAK.Marketplaces.Cimri;

public static class CimriRetailOfferPolicyResolver
{
    public static CimriRetailOfferRuntimePolicy Resolve(
        CimriClientOptions options,
        CimriRetailOfferJobPolicy? jobPolicy)
    {
        if (jobPolicy == null)
        {
            return FromOptionsOnly(options);
        }

        var substrings = jobPolicy.AllowedMerchantSubstrings is { Count: > 0 }
            ? jobPolicy.AllowedMerchantSubstrings
            : NormalizeDefaultList(options.AllowedMerchantNameSubstrings);

        return new CimriRetailOfferRuntimePolicy(
            jobPolicy.RestrictToAllowedMerchants,
            substrings,
            jobPolicy.RequireMerchantProductId,
            jobPolicy.SkipProductIfNoQualifiedOffers);
    }

    public static CimriRetailOfferRuntimePolicy MergeFromSyncInput(
        CimriClientOptions options,
        CimriListingSyncInput input)
    {
        var restrict = input.RestrictOffersToAllowedMerchants ?? options.RestrictOffersToAllowedMerchants;
        var requireId = input.RequireMerchantProductId ?? options.RequireMerchantProductId;
        var skip = input.SkipProductWithoutQualifiedOffers ?? options.SkipProductWithoutQualifiedOffers;

        var substrings = input.AllowedMerchantSubstrings is { Count: > 0 }
            ? input.AllowedMerchantSubstrings
            : NormalizeDefaultList(options.AllowedMerchantNameSubstrings);

        return new CimriRetailOfferRuntimePolicy(restrict, substrings, requireId, skip);
    }

    public static CimriRetailOfferJobPolicy ToJobPolicy(CimriRetailOfferRuntimePolicy policy)
    {
        return new CimriRetailOfferJobPolicy
        {
            RestrictToAllowedMerchants = policy.RestrictToAllowedMerchants,
            RequireMerchantProductId = policy.RequireMerchantProductId,
            SkipProductIfNoQualifiedOffers = policy.SkipProductIfNoQualifiedOffers,
            AllowedMerchantSubstrings = policy.AllowedMerchantSubstrings.ToList(),
        };
    }

    private static CimriRetailOfferRuntimePolicy FromOptionsOnly(CimriClientOptions options)
    {
        return new CimriRetailOfferRuntimePolicy(
            options.RestrictOffersToAllowedMerchants,
            NormalizeDefaultList(options.AllowedMerchantNameSubstrings),
            options.RequireMerchantProductId,
            options.SkipProductWithoutQualifiedOffers);
    }

    private static IReadOnlyList<string> NormalizeDefaultList(IReadOnlyList<string>? list)
    {
        if (list == null || list.Count == 0)
        {
            return CimriDefaultAllowedMerchants.Substrings;
        }

        return list.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }
}
