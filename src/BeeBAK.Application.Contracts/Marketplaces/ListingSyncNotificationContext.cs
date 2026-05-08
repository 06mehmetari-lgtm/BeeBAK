using System;
using BeeBAK.Marketplaces;

namespace BeeBAK.Marketplaces;

public sealed record ListingSyncNotificationContext(
    MarketplaceKind Marketplace,
    Guid ScrapeRunId,
    int ProductsAffected,
    int PagesFetched,
    string SearchQuery);
