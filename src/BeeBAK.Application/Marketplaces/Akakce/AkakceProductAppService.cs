using System;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceProductAppService : ApplicationService, IAkakceProductAppService
{
    private readonly IAkakceProductRepository _productRepository;
    private readonly IRepository<AkakceMerchant, Guid> _merchantRepository;
    private readonly IAkakceStoredDataCleaner _storedDataCleaner;

    public AkakceProductAppService(
        IAkakceProductRepository productRepository,
        IRepository<AkakceMerchant, Guid> merchantRepository,
        IAkakceStoredDataCleaner storedDataCleaner)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _storedDataCleaner = storedDataCleaner;
    }

    [Authorize(BeeBAKPermissions.Akakce.Sync)]
    public virtual Task ClearAllStoredDataAsync()
    {
        return _storedDataCleaner.ClearAllAsync();
    }

    [Authorize(BeeBAKPermissions.Akakce.Default)]
    public virtual async Task<AkakceProductDto> GetAsync(Guid id)
    {
        var product = await _productRepository.GetAsync(id);
        var withOffers = await _productRepository.FindByProductCodeAsync(product.ProductCode, includeOffers: true);
        if (withOffers == null)
        {
            throw new EntityNotFoundException(typeof(AkakceProduct), id);
        }

        return await MapWithMerchantsAsync(withOffers);
    }

    [Authorize(BeeBAKPermissions.Akakce.Default)]
    public virtual async Task<PagedResultDto<AkakceProductDto>> GetListAsync(GetAkakceProductListInput input)
    {
        var maxResult = input.MaxResultCount <= 0 ? 24 : Math.Min(input.MaxResultCount, 100);
        var skip = Math.Max(0, input.SkipCount);

        var (items, totalCount) = await _productRepository.GetPagedAsync(
            skip,
            maxResult,
            input.Search,
            input.IncludeOffers);

        var dtos = new System.Collections.Generic.List<AkakceProductDto>();
        foreach (var product in items)
        {
            dtos.Add(await MapWithMerchantsAsync(product));
        }

        return new PagedResultDto<AkakceProductDto>(totalCount, dtos);
    }

    private async Task<AkakceProductDto> MapWithMerchantsAsync(AkakceProduct product)
    {
        var dto = new AkakceProductDto
        {
            Id = product.Id,
            ProductCode = product.ProductCode,
            ProductUrl = product.ProductUrl,
            Title = product.Title,
            BrandName = product.BrandName,
            PrimaryImageUrl = product.PrimaryImageUrl,
            CategoryPath = product.CategoryPath,
            DiscountPercent = product.DiscountPercent,
            BestPriceAmount = product.BestPriceAmount,
            PreviousPriceAmount = product.PreviousPriceAmount,
            OfferCount = product.OfferCount,
            IsActive = product.IsActive,
            LastSyncedUtc = product.LastSyncedUtc,
        };

        if (product.Offers.Count == 0)
        {
            return dto;
        }

        var merchantIds = product.Offers.Select(o => o.MerchantId).Distinct().ToList();
        var merchantQ = await _merchantRepository.GetQueryableAsync();
        var merchants = merchantQ.Where(m => merchantIds.Contains(m.Id)).ToList().ToDictionary(m => m.Id, m => m);

        foreach (var offer in product.Offers.OrderBy(o => o.DisplayOrder))
        {
            merchants.TryGetValue(offer.MerchantId, out var merchant);
            dto.Offers.Add(new AkakceOfferDto
            {
                Id = offer.Id,
                ProductId = offer.ProductId,
                MerchantId = offer.MerchantId,
                MerchantName = merchant?.Name ?? string.Empty,
                MerchantLogoUrl = merchant?.LogoUrl,
                DisplayOrder = offer.DisplayOrder,
                OfferTitle = offer.OfferTitle,
                SellerName = offer.SellerName,
                Price = offer.Price,
                Currency = offer.Currency,
                ShippingText = offer.ShippingText,
                ShippingAmount = offer.ShippingAmount,
                IsFreeShipping = offer.IsFreeShipping,
                StockText = offer.StockText,
                StockQuantity = offer.StockQuantity,
                DeliveryText = offer.DeliveryText,
                LastUpdatedText = offer.LastUpdatedText,
                LastUpdatedUtc = offer.LastUpdatedUtc,
                OfferUrl = offer.OfferUrl,
                MerchantProductUrl = offer.MerchantProductUrl,
                SiteRedirectUrl = offer.SiteRedirectUrl,
                ScrapedUtc = offer.ScrapedUtc,
            });
        }

        return dto;
    }
}
