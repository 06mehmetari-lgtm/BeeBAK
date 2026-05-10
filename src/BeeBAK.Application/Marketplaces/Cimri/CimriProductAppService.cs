using System;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriProductAppService : ApplicationService, ICimriProductAppService
{
    private readonly ICimriProductRepository _productRepository;
    private readonly IRepository<CimriMerchant, Guid> _merchantRepository;
    private readonly ICimriStoredDataCleaner _cimriStoredDataCleaner;

    public CimriProductAppService(
        ICimriProductRepository productRepository,
        IRepository<CimriMerchant, Guid> merchantRepository,
        ICimriStoredDataCleaner cimriStoredDataCleaner)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _cimriStoredDataCleaner = cimriStoredDataCleaner;
    }

    [Authorize(BeeBAKPermissions.Cimri.Sync)]
    public virtual async Task ClearAllStoredDataAsync()
    {
        await _cimriStoredDataCleaner.ClearAllAsync();
    }

    [Authorize(BeeBAKPermissions.Cimri.Default)]
    public virtual async Task<CimriProductDto> GetAsync(Guid id)
    {
        var product = await _productRepository.GetAsync(id);
        var queryable = await _productRepository.GetQueryableAsync();

        // Re-load with offers via repository (FindByContentIdAsync supports include)
        var withOffers = await _productRepository.FindByContentIdAsync(product.ContentId, includeOffers: true);
        if (withOffers == null)
        {
            throw new EntityNotFoundException(typeof(CimriProduct), id);
        }

        return await MapWithMerchantsAsync(withOffers);
    }

    [Authorize(BeeBAKPermissions.Cimri.Default)]
    public virtual async Task<PagedResultDto<CimriProductDto>> GetListAsync(GetCimriProductListInput input)
    {
        var maxResult = input.MaxResultCount <= 0 ? 24 : Math.Min(input.MaxResultCount, 100);
        var skip = Math.Max(0, input.SkipCount);

        var (items, totalCount) = await _productRepository.GetPagedAsync(
            skipCount: skip,
            maxResultCount: maxResult,
            search: input.Search,
            includeOffers: input.IncludeOffers);

        var dtos = new System.Collections.Generic.List<CimriProductDto>();
        foreach (var p in items)
        {
            dtos.Add(await MapWithMerchantsAsync(p));
        }

        return new PagedResultDto<CimriProductDto>(totalCount, dtos);
    }

    private async Task<CimriProductDto> MapWithMerchantsAsync(CimriProduct product)
    {
        var dto = new CimriProductDto
        {
            Id = product.Id,
            ContentId = product.ContentId,
            ProductUrl = product.ProductUrl,
            Title = product.Title,
            BrandName = product.BrandName,
            PrimaryCategorySlug = product.PrimaryCategorySlug,
            CategoryPath = product.CategoryPath,
            PrimaryImageUrl = product.PrimaryImageUrl,
            TotalOfferCount = product.TotalOfferCount,
            DiscountPercent = product.DiscountPercent,
            BestPriceAmount = product.BestPriceAmount,
            BestPriceMerchantName = product.BestPriceMerchantName,
            PreviousPriceAmount = product.PreviousPriceAmount,
            LastSyncedUtc = product.LastSyncedUtc,
        };

        if (product.Offers.Count == 0)
        {
            return dto;
        }

        var merchantIds = product.Offers.Select(o => o.MerchantId).Distinct().ToList();
        var merchantQ = await _merchantRepository.GetQueryableAsync();
        var merchantsList = merchantQ.Where(m => merchantIds.Contains(m.Id)).ToList();
        var merchantsById = merchantsList.ToDictionary(m => m.Id, m => m);

        foreach (var offer in product.Offers.OrderBy(o => o.DisplayOrder))
        {
            merchantsById.TryGetValue(offer.MerchantId, out var merchant);

            dto.Offers.Add(new CimriOfferDto
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
                PromotionText = offer.PromotionText,
                LastUpdatedText = offer.LastUpdatedText,
                LastUpdatedUtc = offer.LastUpdatedUtc,
                InstallmentBadge = offer.InstallmentBadge,
                MerchantScore = offer.MerchantScore,
                IsSponsored = offer.IsSponsored,
                IsCheapest = offer.IsCheapest,
                YearsOnCimri = offer.YearsOnCimri,
                OfferUrl = offer.OfferUrl,
                MerchantProductUrl = offer.MerchantProductUrl,
                MerchantProductId = offer.MerchantProductId,
                ScrapedUtc = offer.ScrapedUtc,
            });
        }

        return dto;
    }
}
