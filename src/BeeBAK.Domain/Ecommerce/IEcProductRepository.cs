using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Marketplaces;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Ecommerce;

public interface IEcProductRepository : IRepository<EcProduct, Guid>
{
    Task<EcProduct?> FindByMarketplaceAndExternalIdAsync(
        MarketplaceKind marketplace,
        string externalProductId,
        bool includePriceSnapshots = false,
        CancellationToken cancellationToken = default);

    /// <summary>Sayfalı liste; her ürün için son fiyat snapshot tek kayıt olarak Include edilir.</summary>
    Task<(IReadOnlyList<EcProduct> Items, int TotalCount)> GetPagedByMarketplaceAsync(
        MarketplaceKind marketplace,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default);
}
