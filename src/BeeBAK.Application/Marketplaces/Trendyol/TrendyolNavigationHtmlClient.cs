using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolNavigationHtmlClient : ITransientDependency
{
    public const string HttpClientName = "Trendyol";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TrendyolClientOptions> _optionsMonitor;

    public TrendyolNavigationHtmlClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TrendyolClientOptions> optionsMonitor)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
    }

    public async Task<string> GetNavigationHtmlAsync(CancellationToken cancellationToken = default)
    {
        var options = _optionsMonitor.CurrentValue;
        var http = _httpClientFactory.CreateClient(HttpClientName);

        var path = string.IsNullOrWhiteSpace(options.NavigationHtmlPath)
            ? "/"
            : options.NavigationHtmlPath.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var baseUrl = options.BaseUrl.TrimEnd('/');
        var uri = new Uri(new Uri(baseUrl + "/", UriKind.Absolute), path.TrimStart('/'));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Referer", baseUrl + "/");
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
}
