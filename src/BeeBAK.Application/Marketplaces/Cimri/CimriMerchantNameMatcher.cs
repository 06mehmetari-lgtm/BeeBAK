using System;
using System.Collections.Generic;

namespace BeeBAK.Marketplaces.Cimri;

public static class CimriMerchantNameMatcher
{
    /// <summary>Mağaza görünen adı, izin verilen alt dizelerden herhangi birini içeriyor mu (LIKE benzeri).</summary>
    public static bool MatchesAnySubstring(string? merchantName, IReadOnlyList<string> allowedSubstrings)
    {
        if (string.IsNullOrWhiteSpace(merchantName) || allowedSubstrings.Count == 0)
        {
            return false;
        }

        var normalizedName = Normalize(merchantName);
        foreach (var raw in allowedSubstrings)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var needle = Normalize(raw.Trim());
            if (needle.Length == 0)
            {
                continue;
            }

            if (normalizedName.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Türkçe İ/I ve birleşik harfleri sadeleştirir; küçük harfe çevirir.</summary>
    public static string Normalize(string value)
    {
        var s = value.Trim().ToLowerInvariant();
        return s
            .Replace('ı', 'i')
            .Replace('İ', 'i')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ş', 's')
            .Replace('ö', 'o')
            .Replace('ç', 'c');
    }
}
