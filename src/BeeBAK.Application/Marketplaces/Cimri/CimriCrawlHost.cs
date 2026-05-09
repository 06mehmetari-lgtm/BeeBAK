using System;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri tarafı URL'lerinin hangi ana makineye izin verileceği — kodda sabit domain yok;
/// <see cref="CimriClientOptions.AllowedCimriHostSuffix"/> ve/veya <see cref="CimriClientOptions.BaseUrl"/> ile belirlenir.
/// </summary>
public static class CimriCrawlHost
{
    /// <summary>
    /// HTTPS URI ana makinesi yapılandırılan Cimri ortamıyla uyumlu mu (suffix veya BaseUrl ile).
    /// </summary>
    public static bool IsAllowedHost(Uri uri, CimriClientOptions options)
    {
        var suffix = options.AllowedCimriHostSuffix?.Trim();
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

    /// <summary>/offer/… biçimindeki Cimri yönlendirme bağlantısı mı (aynı ortam ana makinesi).</summary>
    public static bool IsCimriOfferRedirectUrl(string url, CimriClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            return false;
        }

        if (!IsAllowedHost(u, options))
        {
            return false;
        }

        return u.AbsolutePath.StartsWith("/offer/", StringComparison.OrdinalIgnoreCase);
    }
}
