using System;
using Volo.Abp;

namespace BeeBAK.Marketplaces.Cimri;

public static class CimriListingUrlResolver
{
    /// <summary>
    /// Önce senkron/API girişindeki tam HTTPS URL, sonra yapılandırmadaki
    /// <see cref="CimriClientOptions.ListingPageUrl"/>.
    /// BaseUrl + path ile otomatik birleştirme yok — hangi liste sayfasının taranacağı açıkça verilmelidir.
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

        throw new BusinessException("BeeBAK:CimriListingUrlNotConfigured")
            .WithData("Reason", "ListingPageUrl");
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
}
