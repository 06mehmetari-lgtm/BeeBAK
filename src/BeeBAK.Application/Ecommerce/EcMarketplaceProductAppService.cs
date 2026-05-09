using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Marketplaces;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace BeeBAK.Ecommerce;

[Authorize]
public class EcMarketplaceProductAppService : ApplicationService, IEcMarketplaceProductAppService
{
    private readonly IEcProductRepository _productRepository;

    public EcMarketplaceProductAppService(IEcProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public virtual async Task<PagedResultDto<EcMarketplaceProductDto>> GetListAsync(
        GetEcMarketplaceProductListInput input)
    {
        await CheckMarketplacePermissionAsync(input.Marketplace);

        var (items, totalCount) = await _productRepository.GetPagedByMarketplaceAsync(
            input.Marketplace,
            input.SkipCount,
            input.MaxResultCount);

        var dtos = items.Select(MapToDto).ToList();

        return new PagedResultDto<EcMarketplaceProductDto>(totalCount, dtos);
    }

    private async Task CheckMarketplacePermissionAsync(MarketplaceKind marketplace)
    {
        switch (marketplace)
        {
            case MarketplaceKind.Cimri:
                await AuthorizationService.CheckAsync(BeeBAKPermissions.Cimri.Default);
                break;
            case MarketplaceKind.Hepsiburada:
                await AuthorizationService.CheckAsync(BeeBAKPermissions.Hepsiburada.Default);
                break;
        }
    }

    private static EcMarketplaceProductDto MapToDto(EcProduct p)
    {
        var latest = p.PriceSnapshots.OrderByDescending(s => s.ScrapedUtc).FirstOrDefault();

        var primaryImage = p.Images
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.ImageUrl)
            .FirstOrDefault(i => i.IsPrimary) ?? p.Images.OrderBy(i => i.SortOrder).FirstOrDefault();

        return new EcMarketplaceProductDto
        {
            Id = p.Id,
            Marketplace = p.Marketplace,
            ExternalProductId = p.ExternalProductId,
            Title = p.Title,
            ProductUrl = p.ProductUrl,
            BrandName = p.BrandName,
            PrimaryImageUrl = primaryImage?.ImageUrl,
            RatingAverage = p.Detail?.RatingAverage,
            ReviewCount = p.Detail?.ReviewCount,
            LastSyncedUtc = p.LastSyncedUtc,
            LatestPriceAmount = latest?.PriceAmount,
            Currency = latest?.Currency,
            ListPriceAmount = latest?.ListPriceAmount,
            DiscountPercent = latest?.DiscountPercent,
        };
    }
}
