using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri'den yakalanan teklif redirect URL'sini takip ederek mağazadaki son ürün URL'sini bulur.
/// </summary>
public interface ICimriOfferUrlResolver
{
    Task<string?> ResolveAsync(string? cimriRedirectUrl, CancellationToken cancellationToken = default);
}

public class CimriOfferUrlResolver : ICimriOfferUrlResolver, ITransientDependency
{
    public const string HttpClientName = "CimriOfferRedirectFollower";
    private const int MaxRedirectHops = 8;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly ILogger<CimriOfferUrlResolver> _logger;

    public CimriOfferUrlResolver(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CimriClientOptions> options,
        ILogger<CimriOfferUrlResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> ResolveAsync(string? cimriRedirectUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cimriRedirectUrl))
        {
            return null;
        }

        if (!TryNormalizeAbsolute(cimriRedirectUrl, out var current))
        {
            return null;
        }

        if (IsExternalMerchantHost(current))
        {
            return current.AbsoluteUri;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var options = _options.CurrentValue;
        if (!string.IsNullOrWhiteSpace(options.UserAgent))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        }

        if (!string.IsNullOrWhiteSpace(options.Cookie))
        {
            client.DefaultRequestHeaders.Add("Cookie", options.Cookie);
        }

        Uri? referrer = null;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && Uri.TryCreate(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var originForRef))
        {
            referrer = originForRef;
        }

        for (var hop = 0; hop < MaxRedirectHops; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage? response = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, current);
                if (referrer != null)
                {
                    request.Headers.Referrer = referrer;
                }

                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == HttpStatusCode.MethodNotAllowed
                    || response.StatusCode == HttpStatusCode.Forbidden
                    || response.StatusCode == HttpStatusCode.BadRequest)
                {
                    response.Dispose();
                    using var getReq = new HttpRequestMessage(HttpMethod.Get, current);
                    if (referrer != null)
                    {
                        getReq.Headers.Referrer = referrer;
                    }

                    response = await client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }

                var statusCode = (int)response.StatusCode;
                if (statusCode is >= 300 and < 400)
                {
                    var loc = response.Headers.Location;
                    if (loc == null)
                    {
                        return current.AbsoluteUri;
                    }

                    var next = loc.IsAbsoluteUri ? loc : new Uri(current, loc);
                    current = next;

                    if (IsExternalMerchantHost(current))
                    {
                        return current.AbsoluteUri;
                    }

                    continue;
                }

                if (response.RequestMessage?.RequestUri is { } finalUri && IsExternalMerchantHost(finalUri))
                {
                    return finalUri.AbsoluteUri;
                }

                return current.AbsoluteUri;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cimri offer redirect takibi başarısız: {Url}", current);
                return null;
            }
            finally
            {
                response?.Dispose();
            }
        }

        return current.AbsoluteUri;
    }

    private bool TryNormalizeAbsolute(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
        {
            uri = abs;
            return true;
        }

        var opt = _options.CurrentValue;
        if (!string.IsNullOrWhiteSpace(opt.BaseUrl)
            && Uri.TryCreate(opt.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var origin)
            && Uri.TryCreate(origin, url, out var rel))
        {
            uri = rel;
            return true;
        }

        uri = default!;
        return false;
    }

    /// <summary>Mağaza sitesine geçildi mi (yapılandırılan Cimri ana makinesi veya cimri.io CDN değil).</summary>
    private bool IsExternalMerchantHost(Uri uri)
    {
        var opt = _options.CurrentValue;
        if (CimriCrawlHost.IsAllowedHost(uri, opt))
        {
            return false;
        }

        if (uri.Host.EndsWith("cimri.io", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
