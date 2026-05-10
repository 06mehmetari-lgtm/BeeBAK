using System;
using Volo.Abp;

namespace BeeBAK.Marketplaces.Akakce;

public static class AkakceListingUrlResolver
{
    public static string Resolve(AkakceClientOptions options, string? listingPageUrlOverride)
    {
        if (!string.IsNullOrWhiteSpace(listingPageUrlOverride))
        {
            return ValidateAndReturn(listingPageUrlOverride.Trim(), options, nameof(listingPageUrlOverride));
        }

        if (!string.IsNullOrWhiteSpace(options.ListingPageUrl))
        {
            return ValidateAndReturn(options.ListingPageUrl.Trim(), options, nameof(options.ListingPageUrl));
        }

        throw new BusinessException("BeeBAK:AkakceListingUrlNotConfigured");
    }

    private static string ValidateAndReturn(string url, AkakceClientOptions options, string fieldHint)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new BusinessException("BeeBAK:AkakceInvalidListingUrl")
                .WithData("Url", url)
                .WithData("Field", fieldHint);
        }

        if (!AkakceCrawlHost.IsAllowedHost(uri, options))
        {
            throw new BusinessException("BeeBAK:AkakceListingUrlHostNotAllowed")
                .WithData("Host", uri.Host);
        }

        return url;
    }

    public static string BuildPageUrl(string listingUrl, int page)
    {
        if (page <= 1)
        {
            return listingUrl;
        }

        var separator = listingUrl.Contains('?') ? "&" : "?";
        var withoutExistingPage = System.Text.RegularExpressions.Regex.Replace(
            listingUrl,
            @"([?&])p=\d+&?",
            "$1",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).TrimEnd('?', '&');

        separator = withoutExistingPage.Contains('?') ? "&" : "?";
        return withoutExistingPage + separator + "p=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
