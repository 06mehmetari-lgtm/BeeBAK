using System.Collections.Generic;
using System.Linq;

namespace BeeBAK.Marketplaces.Akakce;

public static class AkakceListingCardSorter
{
    public static IReadOnlyList<AkakceListingCard> SortByDiscountDescending(IEnumerable<AkakceListingCard> cards)
    {
        return cards
            .OrderByDescending(x => x.DiscountPercent ?? -1m)
            .ThenBy(x => x.Title)
            .ToList();
    }
}
