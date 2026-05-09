using System;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Marketplaces.Cimri;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace BeeBAK.EntityFrameworkCore.Cimri;

public class CimriMerchantRepository :
    EfCoreRepository<BeeBAKDbContext, CimriMerchant, Guid>,
    ICimriMerchantRepository
{
    public CimriMerchantRepository(IDbContextProvider<BeeBAKDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<CimriMerchant?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var query = await GetQueryableAsync();
        return await query.FirstOrDefaultAsync(x => x.Slug == slug, GetCancellationToken(cancellationToken));
    }
}
