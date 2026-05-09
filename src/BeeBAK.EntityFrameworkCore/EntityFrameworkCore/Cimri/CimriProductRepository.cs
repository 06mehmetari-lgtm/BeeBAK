using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Marketplaces.Cimri;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace BeeBAK.EntityFrameworkCore.Cimri;

public class CimriProductRepository :
    EfCoreRepository<BeeBAKDbContext, CimriProduct, Guid>,
    ICimriProductRepository
{
    public CimriProductRepository(IDbContextProvider<BeeBAKDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<CimriProduct?> FindByContentIdAsync(
        string contentId,
        bool includeOffers = false,
        CancellationToken cancellationToken = default)
    {
        var query = (await GetQueryableAsync())
            .Where(x => x.ContentId == contentId);

        if (includeOffers)
        {
            query = query.Include(x => x.Offers);
        }

        return await query.FirstOrDefaultAsync(GetCancellationToken(cancellationToken));
    }

    public async Task<(IReadOnlyList<CimriProduct> Items, int TotalCount)> GetPagedAsync(
        int skipCount,
        int maxResultCount,
        string? search = null,
        bool includeOffers = false,
        CancellationToken cancellationToken = default)
    {
        var token = GetCancellationToken(cancellationToken);
        var query = await GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Title, pattern)
                || (x.BrandName != null && EF.Functions.ILike(x.BrandName, pattern))
                || (x.CategoryPath != null && EF.Functions.ILike(x.CategoryPath, pattern)));
        }

        var totalCount = await query.CountAsync(token);

        query = query
            .OrderByDescending(x => x.LastSyncedUtc.HasValue)
            .ThenByDescending(x => x.LastSyncedUtc)
            .Skip(skipCount)
            .Take(maxResultCount);

        if (includeOffers)
        {
            query = query.Include(x => x.Offers);
        }

        var list = await query.ToListAsync(token);
        return (list, totalCount);
    }
}
