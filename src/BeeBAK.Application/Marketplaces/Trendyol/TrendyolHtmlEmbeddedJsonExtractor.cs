using System;

namespace BeeBAK.Marketplaces.Trendyol;

/// <summary>
/// Pulls JSON embedded in Trendyol HTML (Next.js <c>__NEXT_DATA__</c>) when XHR JSON endpoints are blocked.
/// </summary>
public static class TrendyolHtmlEmbeddedJsonExtractor
{
    public static string? TryExtractNextDataJson(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var nextData = ExtractScriptBody(html, "__NEXT_DATA__");
        if (!string.IsNullOrWhiteSpace(nextData))
        {
            return nextData.Trim();
        }

        return null;
    }

    private static string? ExtractScriptBody(string html, string scriptId)
    {
        var idNeedle = $"id=\"{scriptId}\"";
        var idx = html.IndexOf(idNeedle, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            idNeedle = $"id='{scriptId}'";
            idx = html.IndexOf(idNeedle, StringComparison.OrdinalIgnoreCase);
        }

        if (idx < 0)
        {
            return null;
        }

        var gt = html.IndexOf('>', idx);
        if (gt < 0)
        {
            return null;
        }

        var start = gt + 1;
        const string endTag = "</script>";
        var end = html.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return null;
        }

        var json = html.Substring(start, end - start);
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }
}
