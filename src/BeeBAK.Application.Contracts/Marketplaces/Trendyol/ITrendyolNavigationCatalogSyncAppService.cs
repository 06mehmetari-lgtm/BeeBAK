using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Trendyol;

public interface ITrendyolNavigationCatalogSyncAppService : IApplicationService
{
    /// <summary>
    /// Ana sayfa HTML alt navigasyonundan segmentleri okur;
    /// <see cref="BeeBAK.Ecommerce.EcTrendyolNavSectionTracker"/> ile eşleşen kodlar için <see cref="BeeBAK.Ecommerce.EcMarketplaceCategory"/> oluşturur veya günceller.
    /// </summary>
    Task<TrendyolNavigationCatalogSyncResultDto> SyncRootSectionsFromNavigationAsync();
}
