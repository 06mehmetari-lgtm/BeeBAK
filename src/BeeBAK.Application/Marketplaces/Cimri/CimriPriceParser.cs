using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BeeBAK.Marketplaces.Cimri;

internal static class CimriPriceParser
{
    private static readonly Regex AmountRegex = new(
        @"(?<num>[\d\.\,]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// "1.282,50 TL", "399,99 TL", "12.740,00 TL" → decimal. Tr-TR sayı formatı (binlik nokta, ondalık virgül).
    /// </summary>
    public static decimal? TryParseTryAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = AmountRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups["num"].Value;
        return TryParseTrNumber(raw);
    }

    public static decimal? TryParseTrNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalized = raw.Replace(".", string.Empty).Replace(',', '.');
        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var v)
            ? v
            : null;
    }

    public static decimal? TryParseScore(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().Replace(',', '.');
        return decimal.TryParse(
            trimmed,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var v)
            ? v
            : null;
    }

    /// <summary>"3 Yıldır Cimri'de" → 3</summary>
    public static int? TryParseYearsBadge(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var m = Regex.Match(text, @"(\d+)\s*Yıl", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : null;
    }

    /// <summary>
    /// "11 dk önce güncellendi" / "Bugün 09:01'de güncellendi" → mümkünse UTC; aksi takdirde null.
    /// </summary>
    public static DateTime? TryEstimateLastUpdated(string? text, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var minutesMatch = Regex.Match(text, @"(\d+)\s*dk\s*önce", RegexOptions.IgnoreCase);
        if (minutesMatch.Success && int.TryParse(minutesMatch.Groups[1].Value, out var minutes))
        {
            return utcNow.AddMinutes(-minutes);
        }

        var hoursMatch = Regex.Match(text, @"(\d+)\s*sa(at)?\s*önce", RegexOptions.IgnoreCase);
        if (hoursMatch.Success && int.TryParse(hoursMatch.Groups[1].Value, out var hours))
        {
            return utcNow.AddHours(-hours);
        }

        var daysMatch = Regex.Match(text, @"(\d+)\s*gün\s*önce", RegexOptions.IgnoreCase);
        if (daysMatch.Success && int.TryParse(daysMatch.Groups[1].Value, out var days))
        {
            return utcNow.AddDays(-days);
        }

        return null;
    }
}
