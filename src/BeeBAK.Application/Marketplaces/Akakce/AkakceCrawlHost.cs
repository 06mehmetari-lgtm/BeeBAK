using System;

namespace BeeBAK.Marketplaces.Akakce;

public static class AkakceCrawlHost
{
    public static bool IsAllowedHost(Uri uri, AkakceClientOptions options)
    {
        var suffix = options.AllowedAkakceHostSuffix?.Trim();
        if (!string.IsNullOrEmpty(suffix))
        {
            return uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (Uri.TryCreate(options.BaseUrl?.Trim(), UriKind.Absolute, out var origin))
        {
            return uri.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase)
                   || uri.Host.EndsWith("." + origin.Host, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
