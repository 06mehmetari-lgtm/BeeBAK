using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolBottomNavigationHtmlParser : ITransientDependency
{
    private static readonly Regex SectionAnchorRegex = new(
        @"<a\s+[^>]*class=""[^""]*\bsection-item\b[^""]*""[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SectionNameRegex = new(
        @"<p\s+class=""section-name""[^>]*>([\s\S]*?)</p>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InnerTagRegex = new("<.*?>", RegexOptions.Singleline | RegexOptions.Compiled);

    public IReadOnlyDictionary<string, TrendyolNavigationSectionParsed> ParseSectionItems(string html)
    {
        var map = new Dictionary<string, TrendyolNavigationSectionParsed>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(html))
        {
            return map;
        }

        foreach (Match m in SectionAnchorRegex.Matches(html))
        {
            var rawHref = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(rawHref) || rawHref[0] != '/')
            {
                continue;
            }

            var inner = m.Groups[2].Value;
            var nm = SectionNameRegex.Match(inner);
            var rawName = nm.Success ? nm.Groups[1].Value : "";
            var displayName = CleanSectionName(rawName);

            var extId = NormalizeExternalCategoryId(rawHref);
            var slug = ExtractSlug(rawHref, extId);

            map[extId] = new TrendyolNavigationSectionParsed(extId, displayName, slug, rawHref);
        }

        return map;
    }

    private static string CleanSectionName(string raw)
    {
        var t = WebUtility.HtmlDecode(raw).Trim();
        t = InnerTagRegex.Replace(t, " ");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    internal static string NormalizeExternalCategoryId(string relativeHref)
    {
        var href = relativeHref.Trim();
        var qIdx = href.IndexOf('?', StringComparison.Ordinal);
        var path = qIdx >= 0 ? href[..qIdx] : href;

        if (path.StartsWith("/butik/liste/", StringComparison.OrdinalIgnoreCase))
        {
            var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length >= 3 &&
                segs[0].Equals("butik", StringComparison.OrdinalIgnoreCase) &&
                segs[1].Equals("liste", StringComparison.OrdinalIgnoreCase))
            {
                return segs[2];
            }
        }

        if (path.Equals("/flas-indirimler", StringComparison.OrdinalIgnoreCase))
        {
            return "flas-indirimler";
        }

        if (path.Equals("/cok-satanlar", StringComparison.OrdinalIgnoreCase))
        {
            var gender = TryGetQueryValue(href, "webGenderId");
            return gender != null ? $"cok-satanlar:w{gender}" : "cok-satanlar";
        }

        return href.TrimStart('/').Replace("/", "_", StringComparison.Ordinal);
    }

    private static string? TryGetQueryValue(string href, string key)
    {
        var idx = href.IndexOf('?', StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var query = href[(idx + 1)..];
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }

    private static string? ExtractSlug(string relativeHref, string externalCategoryId)
    {
        if (!relativeHref.StartsWith("/butik/liste/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = relativeHref.Split('?', 2)[0];
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length >= 4 &&
            segs[0].Equals("butik", StringComparison.OrdinalIgnoreCase) &&
            segs[1].Equals("liste", StringComparison.OrdinalIgnoreCase) &&
            segs[2] == externalCategoryId)
        {
            return segs[3];
        }

        return null;
    }
}
