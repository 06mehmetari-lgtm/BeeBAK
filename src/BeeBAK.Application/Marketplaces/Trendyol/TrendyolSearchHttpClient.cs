using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolSearchHttpClient : ITransientDependency
{
    public const string HttpClientName = "Trendyol";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TrendyolClientOptions> _optionsMonitor;

    public TrendyolSearchHttpClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TrendyolClientOptions> optionsMonitor)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
    }

    public Task<string> GetSearchJsonAsync(
        string searchQuery,
        int page,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        return GetSearchJsonAsync(options.SearchApiPath, searchQuery, page, cancellationToken);
    }

    public async Task<string> GetSearchJsonAsync(
        string searchApiPath,
        string searchQuery,
        int page,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var http = _httpClientFactory.CreateClient(HttpClientName);

        var uri = BuildRequestUri(options, searchApiPath, searchQuery, page);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        ApplyJsonRequestHeaders(request, options);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetSearchHtmlAsync(
        string searchQuery,
        int page,
        CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var http = _httpClientFactory.CreateClient(HttpClientName);

        var uri = BuildHtmlSearchUri(options, searchQuery, page);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Referer", options.Referer);
        if (!string.IsNullOrWhiteSpace(options.AcceptLanguage))
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", options.AcceptLanguage!);
        }

        if (!string.IsNullOrWhiteSpace(options.Cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", options.Cookie!);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    internal static Uri BuildRequestUri(
        TrendyolClientOptions options,
        string searchApiPath,
        string searchQuery,
        int page)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var path = searchApiPath.Trim().TrimStart('/');
        if (!Uri.TryCreate($"{baseUrl}/{path}", UriKind.Absolute, out var pathUri))
        {
            throw new ArgumentException("Invalid Trendyol BaseUrl or SearchApiPath.");
        }

        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.DefaultQueryParameters != null)
        {
            foreach (var kv in options.DefaultQueryParameters.Where(kv => !string.IsNullOrEmpty(kv.Key)))
            {
                pairs[kv.Key] = kv.Value;
            }
        }

        pairs["q"] = searchQuery;
        pairs["page"] = page.ToString();

        var query = string.Join("&",
            pairs.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? string.Empty)}"));

        var builder = new UriBuilder(pathUri) { Query = query };
        return builder.Uri;
    }

    internal static Uri BuildHtmlSearchUri(TrendyolClientOptions options, string searchQuery, int page)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var segment = options.HtmlSearchPath.Trim().Trim('/');
        if (!Uri.TryCreate($"{baseUrl}/{segment}", UriKind.Absolute, out var pathUri))
        {
            throw new ArgumentException("Invalid Trendyol BaseUrl or HtmlSearchPath.");
        }

        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options.DefaultQueryParameters != null)
        {
            foreach (var kv in options.DefaultQueryParameters.Where(kv => !string.IsNullOrEmpty(kv.Key)))
            {
                pairs[kv.Key] = kv.Value;
            }
        }

        pairs["q"] = searchQuery;
        var pageParam = options.HtmlPageQueryParameterName.Trim();
        if (string.IsNullOrEmpty(pageParam))
        {
            pageParam = "pi";
        }

        pairs[pageParam] = page.ToString();

        var query = string.Join("&",
            pairs.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv =>
                    $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? string.Empty)}"));

        var builder = new UriBuilder(pathUri) { Query = query };
        return builder.Uri;
    }

    private static void ApplyJsonRequestHeaders(HttpRequestMessage request, TrendyolClientOptions options)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", options.Accept);
        request.Headers.TryAddWithoutValidation("Referer", options.Referer);
        if (!string.IsNullOrWhiteSpace(options.AcceptLanguage))
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", options.AcceptLanguage!);
        }

        if (!string.IsNullOrWhiteSpace(options.Cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", options.Cookie!);
        }
    }
}
