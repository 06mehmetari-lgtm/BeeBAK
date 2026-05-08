using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Trendyol;

/// <summary>
/// Tries primary JSON API, configured fallbacks, then HTML page embedding (<see cref="TrendyolHtmlEmbeddedJsonExtractor"/>).
/// </summary>
public class TrendyolSearchPayloadProvider : ITransientDependency
{
    private readonly TrendyolSearchHttpClient _httpClient;
    private readonly TrendyolSearchJsonParser _parser;
    private readonly IOptionsMonitor<TrendyolClientOptions> _optionsMonitor;

    public TrendyolSearchPayloadProvider(
        TrendyolSearchHttpClient httpClient,
        TrendyolSearchJsonParser parser,
        IOptionsMonitor<TrendyolClientOptions> optionsMonitor)
    {
        _httpClient = httpClient;
        _parser = parser;
        _optionsMonitor = optionsMonitor;
    }

    public async Task<string> FetchSearchPayloadAsync(
        string searchQuery,
        int page,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var origin = options.BaseUrl.TrimEnd('/');

        var paths = new List<string> { options.SearchApiPath.Trim() };
        if (options.FallbackSearchApiPaths != null)
        {
            foreach (var p in options.FallbackSearchApiPaths)
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    paths.Add(p.Trim());
                }
            }
        }

        Exception? lastException = null;

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string body;
            try
            {
                body = await _httpClient.GetSearchJsonAsync(path, searchQuery, page, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                continue;
            }

            if (LooksLikeHtml(body))
            {
                continue;
            }

            if (_parser.Parse(body, origin).Count > 0)
            {
                return body;
            }

            if (ContainsProductsJsonSignal(body))
            {
                return body;
            }
        }

        if (options.EnableHtmlFallback)
        {
            try
            {
                var html = await _httpClient.GetSearchHtmlAsync(searchQuery, page, cancellationToken);
                var extracted = TrendyolHtmlEmbeddedJsonExtractor.TryExtractNextDataJson(html);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return extracted;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw new BusinessException("BeeBAK:TrendyolFetchExhausted")
            .WithData("Reason", lastException?.Message ?? string.Empty);
    }

    private static bool LooksLikeHtml(string body)
    {
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        return trimmed.StartsWith("<!", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsProductsJsonSignal(string body)
    {
        return body.Contains("\"products\"", StringComparison.Ordinal);
    }
}
