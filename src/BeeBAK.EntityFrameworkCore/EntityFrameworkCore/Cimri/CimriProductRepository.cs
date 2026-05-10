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

        // En yüksek indirim önce; NULL son (PostgreSQL ile uyumlu tek ifade)
        query = query
            .OrderByDescending(x => x.DiscountPercent ?? -1m)
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

    public async Task<List<CimriProduct>> GetShareDeckCandidatesAsync(
        int take,
        IReadOnlyCollection<string> excludeContentIds,
        CancellationToken cancellationToken = default)
    {
        var token = GetCancellationToken(cancellationToken);
        var exclude = excludeContentIds.Count > 0 ? excludeContentIds.ToHashSet() : null;

        // Havuz: aktif ürünler; indirim / görsel şartını SQL'de sıkı tutmayıp bellekte süzeriz —
        // yeni çekilen kayıtlarda DiscountPercent veya görsel henüz dolu olmayabilir.
        var query = (await GetQueryableAsync()).Where(p => p.IsActive);

        if (exclude != null && exclude.Count > 0)
        {
            query = query.Where(p => !exclude.Contains(p.ContentId));
        }

        var fetch = Math.Max(take * 6, 64);

        query = query
            .Include(p => p.Offers)
            .OrderByDescending(p => p.DiscountPercent ?? -1m)
            .ThenByDescending(p => p.LastSyncedUtc)
            .Take(fetch);

        var list = await query.ToListAsync(token);

        return list
            .Where(HasMeaningfulDiscount)
            .Where(p => p.Offers.Any(o =>
                !string.IsNullOrWhiteSpace(o.MerchantProductUrl) || !string.IsNullOrWhiteSpace(o.OfferUrl)))
            .Take(take)
            .ToList();
    }

    private static bool HasMeaningfulDiscount(CimriProduct p)
    {
        if (p.DiscountPercent is > 0m)
        {
            return true;
        }

        return p.PreviousPriceAmount is > 0m
            && p.BestPriceAmount is > 0m
            && p.PreviousPriceAmount > p.BestPriceAmount;
    }
}
