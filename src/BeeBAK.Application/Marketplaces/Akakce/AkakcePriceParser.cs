using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BeeBAK.Marketplaces.Akakce;

internal static class AkakcePriceParser
{
    private static readonly Regex AmountRegex = new(@"(?<num>[\d\.\,]+)", RegexOptions.Compiled);
    private static readonly Regex TryAmountRegex = new(
        @"(?<num>\d{1,3}(?:\.\d{3})*(?:,\d{1,2})?|\d+(?:,\d{1,2})?)\s*TL",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        return TryParseTrNumber(match.Groups["num"].Value);
    }

    public static decimal? TryParseFirstTryAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (Match match in TryAmountRegex.Matches(text))
        {
            var parsed = TryParseTrNumber(match.Groups["num"].Value);
            if (parsed.HasValue)
            {
                return parsed.Value;
            }
        }

        return null;
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

    public static DateTime? TryEstimateLastUpdated(string? text, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var minutesMatch = Regex.Match(text, @"(\d+)\s*dk", RegexOptions.IgnoreCase);
        if (minutesMatch.Success && int.TryParse(minutesMatch.Groups[1].Value, out var minutes))
        {
            return utcNow.AddMinutes(-minutes);
        }

        var todayMatch = Regex.Match(text, @"Bug[uü]n\s+(?<hh>\d{1,2}):(?<mm>\d{2})", RegexOptions.IgnoreCase);
        if (todayMatch.Success
            && int.TryParse(todayMatch.Groups["hh"].Value, out var hh)
            && int.TryParse(todayMatch.Groups["mm"].Value, out var mm))
        {
            return utcNow.Date.AddHours(hh).AddMinutes(mm);
        }

        return null;
    }
}
