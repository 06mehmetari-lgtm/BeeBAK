using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces;
using BeeBAK.Marketplaces.Akakce;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace BeeBAK.EntityFrameworkCore.Akakce;

public class AkakceStoredDataCleaner : IAkakceStoredDataCleaner, ITransientDependency
{
    private readonly IDbContextProvider<BeeBAKDbContext> _dbContextProvider;

    public AkakceStoredDataCleaner(IDbContextProvider<BeeBAKDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        var db = await _dbContextProvider.GetDbContextAsync();

        var runIds = await db.Set<EcScrapeRun>()
            .IgnoreQueryFilters()
            .Where(r => r.Marketplace == MarketplaceKind.Akakce)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (runIds.Count > 0)
        {
            await db.Set<EcScrapeRunEvent>()
                .IgnoreQueryFilters()
                .Where(e => runIds.Contains(e.ScrapeRunId))
                .ExecuteDeleteAsync(cancellationToken);

            await db.Set<EcScrapeRun>()
                .IgnoreQueryFilters()
                .Where(r => r.Marketplace == MarketplaceKind.Akakce)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.Set<AkakceOffer>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await db.Set<AkakceProduct>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await db.Set<AkakceMerchant>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
    }
}
