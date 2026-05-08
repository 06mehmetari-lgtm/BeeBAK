using System.Collections.Generic;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolClientOptions
{
    public const string SectionName = "Trendyol";

    /// <summary>Site origin, e.g. https://www.trendyol.com</summary>
    public string BaseUrl { get; set; } = "https://www.trendyol.com";

    /// <summary>Path only, e.g. /api/sr/search</summary>
    public string SearchApiPath { get; set; } = "/api/sr/search";

    /// <summary>
    /// Extra JSON API paths tried in order when the primary <see cref="SearchApiPath"/> fails or returns no usable products.
    /// Example: /api/discovery/search
    /// </summary>
    public List<string>? FallbackSearchApiPaths { get; set; }

    /// <summary>
    /// When true, after JSON endpoints fail, fetches the public HTML search page and extracts embedded Next.js JSON (<c>__NEXT_DATA__</c>).
    /// </summary>
    public bool EnableHtmlFallback { get; set; } = true;

    /// <summary>HTML search route segment without slashes, e.g. sr → https://host/sr?q=...</summary>
    public string HtmlSearchPath { get; set; } = "sr";

    /// <summary>Query parameter name for the result page index on the HTML search route (Trendyol commonly uses pi).</summary>
    public string HtmlPageQueryParameterName { get; set; } = "pi";

    /// <summary>Extra query pairs copied from DevTools (e.g. mid, os).</summary>
    public Dictionary<string, string>? DefaultQueryParameters { get; set; }

    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public string Accept { get; set; } = "application/json";

    public string Referer { get; set; } = "https://www.trendyol.com/";

    public string? AcceptLanguage { get; set; } = "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7";

    /// <summary>Optional browser cookie header if responses require session.</summary>
    public string? Cookie { get; set; }

    public int DelayBetweenRequestsMs { get; set; } = 1500;

    public int DefaultMaxPages { get; set; } = 5;

    public int RequestTimeoutSeconds { get; set; } = 60;

    public int CacheDurationSeconds { get; set; } = 900;

    public TrendyolTelegramNotificationOptions Telegram { get; set; } = new();
}

public class TrendyolTelegramNotificationOptions
{
    public string? BotToken { get; set; }

    public string? ChatId { get; set; }
}
