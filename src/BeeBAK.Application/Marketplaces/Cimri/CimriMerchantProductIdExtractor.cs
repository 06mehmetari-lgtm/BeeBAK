using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Mağazanın ürün sayfası URL'sinden satıcı tarafındaki ürün id/SKU değerini çıkarmaya çalışır.
/// </summary>
public static class CimriMerchantProductIdExtractor
{
    private static readonly Regex HepsiburadaSkuRegex = new(
        @"\b(HBC?V[0-9A-Z]{6,32})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HepsiburadaPSuffixRegex = new(
        @"-p-([A-Za-z0-9]+)",
        RegexOptions.Compiled);

    private static readonly Regex TrendyolPSuffixRegex = new(
        @"-p-([0-9]{4,})",
        RegexOptions.Compiled);

    private static readonly Regex AmazonAsinRegex = new(
        @"/(?:dp|gp/product|product)/([A-Z0-9]{8,12})",
        RegexOptions.Compiled);

    private static readonly Regex AmazonAsinQueryRegex = new(
        @"[?&]asin=([A-Z0-9]{8,12})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex N11SuffixRegex = new(
        @"-P([0-9]{4,})",
        RegexOptions.Compiled);

    private static readonly Regex IdefixSuffixRegex = new(
        @"-(?:p-)?([0-9]{6,})",
        RegexOptions.Compiled);

    private static readonly Regex GenericTrailingNumericRegex = new(
        @"(\d{5,})(?:[/?#]|$)",
        RegexOptions.Compiled);

    public static string? TryExtract(string? merchantUrl, string? merchantHostHint = null)
    {
        if (string.IsNullOrWhiteSpace(merchantUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(merchantUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(merchantHostHint))
        {
            host = merchantHostHint.ToLowerInvariant();
        }

        var path = uri.AbsolutePath;
        var fullUrl = uri.AbsoluteUri;

        if (Contains(host, "hepsiburada"))
        {
            var sku = HepsiburadaSkuRegex.Match(path).Value;
            if (string.IsNullOrEmpty(sku))
            {
                sku = HepsiburadaSkuRegex.Match(fullUrl).Value;
            }
            if (!string.IsNullOrEmpty(sku))
            {
                return sku.ToUpperInvariant();
            }

            var pSuffix = HepsiburadaPSuffixRegex.Match(path);
            if (pSuffix.Success)
            {
                return pSuffix.Groups[1].Value;
            }
        }

        if (Contains(host, "trendyol"))
        {
            var match = TrendyolPSuffixRegex.Match(path);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        if (Contains(host, "amazon"))
        {
            var match = AmazonAsinRegex.Match(path);
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpperInvariant();
            }

            var qmatch = AmazonAsinQueryRegex.Match(uri.Query);
            if (qmatch.Success)
            {
                return qmatch.Groups[1].Value.ToUpperInvariant();
            }
        }

        if (Contains(host, "n11"))
        {
            var match = N11SuffixRegex.Match(path);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        if (Contains(host, "idefix"))
        {
            var match = IdefixSuffixRegex.Match(path);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        if (Contains(host, "pttavm"))
        {
            var pSuffix = HepsiburadaPSuffixRegex.Match(path);
            if (pSuffix.Success)
            {
                return pSuffix.Groups[1].Value;
            }
        }

        if (Contains(host, "pazarama"))
        {
            var pSuffix = HepsiburadaPSuffixRegex.Match(path);
            if (pSuffix.Success)
            {
                return pSuffix.Groups[1].Value;
            }
        }

        var queryProductIdKeys = new[] { "productid", "productId", "p", "pid", "id", "sku" };
        foreach (var key in queryProductIdKeys)
        {
            var value = ParseQueryValue(uri.Query, key);
            if (!string.IsNullOrWhiteSpace(value) && value.Length <= 64)
            {
                return value;
            }
        }

        var generic = GenericTrailingNumericRegex.Match(path + "/");
        if (generic.Success)
        {
            return generic.Groups[1].Value;
        }

        var lastSegment = path.TrimEnd('/').Split('/').LastOrDefault();
        if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment.Length <= 64)
        {
            return lastSegment;
        }

        return null;
    }

    private static bool Contains(string host, string fragment)
    {
        return host.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseQueryValue(string query, string key)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var k = pair[..eq];
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        return null;
    }
}
