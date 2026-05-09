using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Cimri;

public interface ICimriProductRepository : IRepository<CimriProduct, Guid>
{
    Task<CimriProduct?> FindByContentIdAsync(
        string contentId,
        bool includeOffers = false,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CimriProduct> Items, int TotalCount)> GetPagedAsync(
        int skipCount,
        int maxResultCount,
        string? search = null,
        bool includeOffers = false,
        CancellationToken cancellationToken = default);
}
