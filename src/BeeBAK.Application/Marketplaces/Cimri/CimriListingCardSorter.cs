using System;
using System.Collections.Generic;
using System.Linq;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri liste HTML'inden çıkan kartları işleme sırasına göre sıralar (en yüksek indirim önce).
/// </summary>
public static class CimriListingCardSorter
{
    public static List<CimriListingCard> SortByDiscountDescending(IEnumerable<CimriListingCard> cards)
    {
        return cards
            .OrderByDescending(c => c.DiscountPercent.HasValue ? 1 : 0)
            .ThenByDescending(c => c.DiscountPercent ?? 0)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
