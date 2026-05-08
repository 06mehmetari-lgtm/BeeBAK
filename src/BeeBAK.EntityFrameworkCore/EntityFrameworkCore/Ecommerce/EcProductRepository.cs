using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace BeeBAK.EntityFrameworkCore.Ecommerce;

public class EcProductRepository :
    EfCoreRepository<BeeBAKDbContext, EcProduct, Guid>,
    IEcProductRepository
{
    public EcProductRepository(IDbContextProvider<BeeBAKDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<EcProduct?> FindByMarketplaceAndExternalIdAsync(
        MarketplaceKind marketplace,
        string externalProductId,
        bool includePriceSnapshots = false,
        CancellationToken cancellationToken = default)
    {
        var query = (await GetQueryableAsync())
            .Where(x => x.Marketplace == marketplace && x.ExternalProductId == externalProductId);

        if (includePriceSnapshots)
        {
            query = query.Include(x => x.PriceSnapshots);
        }

        return await query.FirstOrDefaultAsync(GetCancellationToken(cancellationToken));
    }

    public async Task<(IReadOnlyList<EcProduct> Items, int TotalCount)> GetPagedByMarketplaceAsync(
        MarketplaceKind marketplace,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        var token = GetCancellationToken(cancellationToken);
        var baseQuery = (await GetQueryableAsync())
            .Where(x => x.Marketplace == marketplace);

        var totalCount = await baseQuery.CountAsync(token);

        var items = await baseQuery
            .OrderByDescending(x => x.LastSyncedUtc.HasValue)
            .ThenByDescending(x => x.LastSyncedUtc)
            .Skip(skipCount)
            .Take(maxResultCount)
            .Include(x => x.PriceSnapshots.OrderByDescending(s => s.ScrapedUtc).Take(1))
            .ToListAsync(token);

        return (items, totalCount);
    }
}
