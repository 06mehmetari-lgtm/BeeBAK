using System.Globalization;
using System.Text;

namespace BeeBAK.Marketplaces.Cimri;

internal static class CimriSlugifier
{
    /// <summary>"Hepsiburada Premium" → "hepsiburada-premium" — Cimri merchant adlarını eşleştirme anahtarına çevirir.</summary>
    public static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        var prevDash = false;
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevDash = false;
            }
            else if (!prevDash && sb.Length > 0)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var result = sb.ToString().Trim('-');
        return result.Length == 0 ? value.ToLowerInvariant().Trim() : result;
    }
}
