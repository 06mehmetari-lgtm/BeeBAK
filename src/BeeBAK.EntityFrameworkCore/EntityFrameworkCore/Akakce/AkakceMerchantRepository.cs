using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Marketplaces.Akakce;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace BeeBAK.EntityFrameworkCore.Akakce;

public class AkakceMerchantRepository :
    EfCoreRepository<BeeBAKDbContext, AkakceMerchant, Guid>,
    IAkakceMerchantRepository
{
    public AkakceMerchantRepository(IDbContextProvider<BeeBAKDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public async Task<AkakceMerchant?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await (await GetQueryableAsync())
            .FirstOrDefaultAsync(x => x.Slug == slug, GetCancellationToken(cancellationToken));
    }
}
