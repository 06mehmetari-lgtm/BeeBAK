using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.EntityFrameworkCore;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Shares;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace BeeBAK.EntityFrameworkCore.Cimri;

public class CimriStoredDataCleaner : ICimriStoredDataCleaner, ITransientDependency
{
    private readonly IDbContextProvider<BeeBAKDbContext> _dbContextProvider;

    public CimriStoredDataCleaner(IDbContextProvider<BeeBAKDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        var db = await _dbContextProvider.GetDbContextAsync();

        // IgnoreQueryFilters: soft-delete ve diğer global filtreler ExecuteDelete ile satır kaçırmasın (tam silme).
        var cimriRunIds = await db.Set<EcScrapeRun>()
            .IgnoreQueryFilters()
            .Where(r => r.Marketplace == MarketplaceKind.Cimri)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (cimriRunIds.Count > 0)
        {
            await db.Set<EcScrapeRunEvent>()
                .IgnoreQueryFilters()
                .Where(e => cimriRunIds.Contains(e.ScrapeRunId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.Set<EcScrapeRun>()
                .IgnoreQueryFilters()
                .Where(r => r.Marketplace == MarketplaceKind.Cimri)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.Set<BeebakShareCardLog>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await db.Set<BeebakShareProductDayBlock>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);

        await db.Set<CimriOffer>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await db.Set<CimriProduct>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await db.Set<CimriMerchant>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
    }
}
