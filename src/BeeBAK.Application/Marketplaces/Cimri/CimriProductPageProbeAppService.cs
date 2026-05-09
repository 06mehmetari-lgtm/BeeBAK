using System;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace BeeBAK.Marketplaces.Cimri;

[Authorize(BeeBAKPermissions.Cimri.Probe)]
public class CimriProductPageProbeAppService : ApplicationService, ICimriProductPageProbeAppService
{
    private readonly ICimriProductRepository _productRepository;
    private readonly ICimriMerchantRepository _merchantRepository;
    private readonly CimriProductDetailScraper _detailScraper;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly ILogger<CimriProductPageProbeAppService> _logger;

    public CimriProductPageProbeAppService(
        ICimriProductRepository productRepository,
        ICimriMerchantRepository merchantRepository,
        CimriProductDetailScraper detailScraper,
        IOptionsMonitor<CimriClientOptions> options,
        ILogger<CimriProductPageProbeAppService> logger)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _detailScraper = detailScraper;
        _options = options;
        _logger = logger;
    }

    public virtual async Task<CimriProductPageProbeResultDto> ProbeProductPageAsync(CimriProductPageProbeInput input)
    {
        Check.NotNull(input, nameof(input));

        var options = _options.CurrentValue;
        var probedUtc = Clock.Now;

        try
        {
            CimriProductDetailScraper.ValidateProductUrl(input.ProductUrl, options);

            var detail = await _detailScraper.FetchAsync(input.ProductUrl, input.ExpandAllOffers, options);
            if (detail == null)
            {
                return new CimriProductPageProbeResultDto
                {
                    Success = false,
                    Message = "PDP HTML parse edilemedi.",
                    ProbedUtc = probedUtc,
                };
            }

            var productDto = MapToDto(detail);
            if (input.Persist)
            {
                var product = await PersistProbeAsync(detail, probedUtc);
                productDto.Id = product.Id;
                productDto.LastSyncedUtc = product.LastSyncedUtc;
                productDto.DiscountPercent = product.DiscountPercent;
                productDto.BestPriceAmount = product.BestPriceAmount;
                productDto.BestPriceMerchantName = product.BestPriceMerchantName;
                productDto.PreviousPriceAmount = product.PreviousPriceAmount;
            }

            return new CimriProductPageProbeResultDto
            {
                Success = true,
                ProbedUtc = probedUtc,
                Product = productDto,
            };
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cimri PDP probe başarısız: {Url}", input.ProductUrl);
            return new CimriProductPageProbeResultDto
            {
                Success = false,
                Message = ex.Message,
                ProbedUtc = probedUtc,
            };
        }
    }

    private async Task<CimriProduct> PersistProbeAsync(CimriProductDetailExtract detail, DateTime utcNow)
    {
        var product = await _productRepository.FindByContentIdAsync(detail.ContentId, includeOffers: true);
        if (product == null)
        {
            product = new CimriProduct(
                GuidGenerator.Create(),
                detail.ContentId,
                detail.ProductUrl,
                detail.Title,
                primaryCategorySlug: detail.CategorySlug,
                brandName: detail.BrandName,
                primaryImageUrl: detail.PrimaryImageUrl);

            await _productRepository.InsertAsync(product, autoSave: true);
        }

        var bestOffer = detail.Offers
            .Where(o => !o.IsSponsored)
            .OrderBy(o => o.Price)
            .FirstOrDefault()
            ?? detail.Offers.OrderBy(o => o.Price).FirstOrDefault();

        product.ApplyListingSnapshot(
            title: detail.Title,
            productUrl: detail.ProductUrl,
            primaryCategorySlug: detail.CategorySlug,
            brandName: detail.BrandName,
            primaryImageUrl: detail.PrimaryImageUrl,
            discountPercent: null,
            totalOfferCount: detail.TotalOfferCount,
            bestPriceAmount: bestOffer?.Price,
            bestPriceMerchantName: bestOffer?.MerchantName,
            previousPriceAmount: null,
            utcNow: utcNow);

        product.ApplyDetailSnapshot(
            categoryPath: detail.CategoryPath,
            brandName: detail.BrandName,
            primaryImageUrl: detail.PrimaryImageUrl,
            totalOfferCount: detail.TotalOfferCount,
            utcNow: utcNow);

        product.ClearOffers();

        foreach (var offerExtract in detail.Offers)
        {
            var merchant = await EnsureMerchantAsync(offerExtract, utcNow);

            var offer = new CimriOffer(
                GuidGenerator.Create(),
                product.Id,
                merchant.Id,
                offerExtract.Price,
                scrapedUtc: utcNow,
                displayOrder: offerExtract.DisplayOrder,
                currency: offerExtract.Currency);

            offer.SetMetadata(
                offerTitle: offerExtract.OfferTitle,
                sellerName: offerExtract.SellerName,
                shippingText: offerExtract.ShippingText,
                promotionText: offerExtract.PromotionText,
                lastUpdatedText: offerExtract.LastUpdatedText,
                lastUpdatedUtc: offerExtract.LastUpdatedUtc,
                installmentBadge: offerExtract.InstallmentBadge,
                merchantScore: offerExtract.MerchantScore,
                isSponsored: offerExtract.IsSponsored,
                isCheapest: offerExtract.IsCheapest,
                yearsOnCimri: offerExtract.YearsOnCimri,
                offerUrl: offerExtract.OfferUrl,
                merchantProductUrl: offerExtract.MerchantProductUrl,
                merchantProductId: offerExtract.MerchantProductId);

            product.AddOffer(offer);
        }

        await _productRepository.UpdateAsync(product, autoSave: true);

        return product;
    }

    private async Task<CimriMerchant> EnsureMerchantAsync(CimriOfferExtract offer, DateTime utcNow)
    {
        var slug = CimriSlugifier.Slugify(offer.MerchantName);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = offer.MerchantName.Trim().ToLowerInvariant();
        }

        var merchant = await _merchantRepository.FindBySlugAsync(slug);
        if (merchant == null)
        {
            merchant = new CimriMerchant(
                GuidGenerator.Create(),
                offer.MerchantName.Trim(),
                slug,
                utcNow);

            merchant.Touch(offer.MerchantLogoUrl, externalMerchantId: null, utcNow);
            await _merchantRepository.InsertAsync(merchant, autoSave: true);
        }
        else
        {
            merchant.Touch(offer.MerchantLogoUrl, externalMerchantId: null, utcNow);
            await _merchantRepository.UpdateAsync(merchant, autoSave: true);
        }

        return merchant;
    }

    private static CimriProductDto MapToDto(CimriProductDetailExtract detail)
    {
        var dto = new CimriProductDto
        {
            ContentId = detail.ContentId,
            ProductUrl = detail.ProductUrl,
            Title = detail.Title,
            BrandName = detail.BrandName,
            PrimaryCategorySlug = detail.CategorySlug,
            CategoryPath = detail.CategoryPath,
            PrimaryImageUrl = detail.PrimaryImageUrl,
            TotalOfferCount = detail.TotalOfferCount,
        };

        var displayOrder = 0;
        foreach (var offer in detail.Offers)
        {
            displayOrder++;
            dto.Offers.Add(new CimriOfferDto
            {
                MerchantName = offer.MerchantName,
                MerchantLogoUrl = offer.MerchantLogoUrl,
                DisplayOrder = offer.DisplayOrder == 0 ? displayOrder : offer.DisplayOrder,
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
            });
        }

        return dto;
    }

}
