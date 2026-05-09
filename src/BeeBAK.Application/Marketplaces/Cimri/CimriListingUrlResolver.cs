using System;
using Volo.Abp;

namespace BeeBAK.Marketplaces.Cimri;

public static class CimriListingUrlResolver
{
    /// <summary>
    /// Önce senkron girişindeki tam URL, sonra <see cref="CimriClientOptions.ListingPageUrl"/>,
    /// son olarak yapılandırmadaki BaseUrl + DiscountedListingPath (ikisi de dolu olmalı).
    /// </summary>
    public static string Resolve(CimriClientOptions options, string? listingPageUrlOverride)
    {
        if (!string.IsNullOrWhiteSpace(listingPageUrlOverride))
        {
            return ValidateAndReturn(listingPageUrlOverride.Trim(), options, nameof(listingPageUrlOverride));
        }

        if (!string.IsNullOrWhiteSpace(options.ListingPageUrl))
        {
            return ValidateAndReturn(options.ListingPageUrl.Trim(), options, nameof(options.ListingPageUrl));
        }

        return BuildFromBaseAndPath(options);
    }

    private static string ValidateAndReturn(string url, CimriClientOptions options, string fieldHint)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new BusinessException("BeeBAK:CimriInvalidListingUrl")
                .WithData("Url", url)
                .WithData("Field", fieldHint);
        }

        if (!CimriCrawlHost.IsAllowedHost(uri, options))
        {
            throw new BusinessException("BeeBAK:CimriListingUrlHostNotAllowed")
                .WithData("Host", uri.Host);
        }

        return url;
    }

    private static string BuildFromBaseAndPath(CimriClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new BusinessException("BeeBAK:CimriListingUrlNotConfigured")
                .WithData("Reason", "BaseUrl");
        }

        if (string.IsNullOrWhiteSpace(options.DiscountedListingPath))
        {
            throw new BusinessException("BeeBAK:CimriListingUrlNotConfigured")
                .WithData("Reason", "DiscountedListingPath");
        }

        var baseUrl = options.BaseUrl.TrimEnd('/');
        var path = options.DiscountedListingPath.Trim();
        path = path.StartsWith('/') ? path : "/" + path;
        var combined = baseUrl + path;

        return ValidateAndReturn(combined, options, nameof(BuildFromBaseAndPath));
    }
}
