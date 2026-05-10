using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Akakce;

public interface IAkakceProductRepository : IRepository<AkakceProduct, Guid>
{
    Task<AkakceProduct?> FindByProductCodeAsync(string productCode, bool includeOffers = false, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AkakceProduct> Items, int TotalCount)> GetPagedAsync(
        int skipCount,
        int maxResultCount,
        string? search = null,
        bool includeOffers = false,
        CancellationToken cancellationToken = default);
}
