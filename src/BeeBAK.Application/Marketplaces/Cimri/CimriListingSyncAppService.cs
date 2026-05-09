using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Cimri;

[Authorize(BeeBAKPermissions.Cimri.Sync)]
public class CimriListingSyncAppService : ApplicationService, ICimriListingSyncAppService
{
    private readonly ICimriProductRepository _productRepository;
    private readonly ICimriMerchantRepository _merchantRepository;
    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly CimriSeleniumPageFetcher _seleniumPageFetcher;
    private readonly CimriProductDetailScraper _detailScraper;
    private readonly IListingSyncNotifier _listingSyncNotifier;
    private readonly ILogger<CimriListingSyncAppService> _logger;

    public CimriListingSyncAppService(
        ICimriProductRepository productRepository,
        ICimriMerchantRepository merchantRepository,
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IOptionsMonitor<CimriClientOptions> options,
        CimriSeleniumPageFetcher seleniumPageFetcher,
        CimriProductDetailScraper detailScraper,
        IListingSyncNotifier listingSyncNotifier,
        ILogger<CimriListingSyncAppService> logger)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _scrapeRunRepository = scrapeRunRepository;
        _options = options;
        _seleniumPageFetcher = seleniumPageFetcher;
        _detailScraper = detailScraper;
        _listingSyncNotifier = listingSyncNotifier;
        _logger = logger;
    }

    public virtual async Task<CimriListingSyncResultDto> SyncAsync(CimriListingSyncInput? input)
    {
        var effectiveInput = input ?? new CimriListingSyncInput();
        var options = _options.CurrentValue;

        var maxPages = NormalizePositive(effectiveInput.MaxPages, options.DefaultMaxPages, 1, 200);
        var maxProducts = NormalizePositive(effectiveInput.MaxProducts, options.DefaultMaxProducts, 1, 5000);
        var includeOffers = effectiveInput.IncludeOffers;
        var expandAllOffers = effectiveInput.ExpandAllOffers;

        var listingUrl = BuildListingUrl(options);
        var loadMoreClicks = Math.Max(0, maxPages - 1);

        var scrapeRun = new EcScrapeRun(
            GuidGenerator.Create(),
            MarketplaceKind.Cimri,
            EcScrapeRunStatus.Running,
            Clock.Now,
            triggerSource: nameof(CimriListingSyncAppService));
        await _scrapeRunRepository.InsertAsync(scrapeRun, autoSave: true);

        var productsAffected = 0;
        var offersAffected = 0;
        var merchantsTouched = new HashSet<Guid>();
        var pagesFetched = 0;

        try
        {
            _logger.LogInformation(
                "Cimri sync başlıyor — {ListingUrl}, maxPages={MaxPages}, maxProducts={MaxProducts}, includeOffers={IncludeOffers}",
                listingUrl, maxPages, maxProducts, includeOffers);

            var html = await _seleniumPageFetcher.TryGetListingHtmlAsync(listingUrl, loadMoreClicks, options);
            if (string.IsNullOrWhiteSpace(html))
            {
                throw new BusinessException("BeeBAK:CimriListingHtmlEmpty")
                    .WithData("ListingUrl", listingUrl);
            }

            var cards = CimriListingHtmlParser.Parse(html, options.BaseUrl);
            pagesFetched = loadMoreClicks + 1;

            _logger.LogInformation(
                "Cimri listeleme: {Count} ayrı ürün kartı parse edildi (loadMoreClicks={Clicks})",
                cards.Count, loadMoreClicks);

            var trimmed = cards.Take(maxProducts).ToList();

            foreach (var card in trimmed)
            {
                try
                {
                    var (productId, addedOffers, touchedMerchantIds) = await UpsertProductAsync(
                        card,
                        includeOffers,
                        expandAllOffers,
                        options);

                    productsAffected++;
                    offersAffected += addedOffers;
                    foreach (var mid in touchedMerchantIds)
                    {
                        merchantsTouched.Add(mid);
                    }

                    if (options.DelayBetweenProductsMs > 0)
                    {
                        await Task.Delay(options.DelayBetweenProductsMs);
                    }

                    _ = productId;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Cimri ürün senkronu başarısız: {ContentId} {ProductUrl}",
                        card.ContentId, card.ProductUrl);
                }
            }

            scrapeRun.Complete(Clock.Now);
            await _scrapeRunRepository.UpdateAsync(scrapeRun, autoSave: true);

            await _listingSyncNotifier.NotifyListingSyncCompletedAsync(
                new ListingSyncNotificationContext(
                    MarketplaceKind.Cimri,
                    scrapeRun.Id,
                    productsAffected,
                    pagesFetched,
                    SearchQuery: options.DiscountedListingPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cimri sync hata aldı");
            scrapeRun.Fail(Clock.Now, ex.Message);
            await _scrapeRunRepository.UpdateAsync(scrapeRun, autoSave: true);
            throw;
        }

        return new CimriListingSyncResultDto
        {
            ScrapeRunId = scrapeRun.Id,
            PagesFetched = pagesFetched,
            ProductsAffected = productsAffected,
            OffersAffected = offersAffected,
            MerchantsAffected = merchantsTouched.Count,
            ResolvedListingPageUrl = listingUrl,
        };
    }

    private async Task<(Guid ProductId, int OffersAdded, HashSet<Guid> TouchedMerchantIds)> UpsertProductAsync(
        CimriListingCard card,
        bool includeOffers,
        bool expandAllOffers,
        CimriClientOptions options)
    {
        var product = await _productRepository.FindByContentIdAsync(card.ContentId, includeOffers: true);
        var utcNow = Clock.Now;

        if (product == null)
        {
            product = new CimriProduct(
                GuidGenerator.Create(),
                card.ContentId,
                card.ProductUrl,
                card.Title,
                primaryCategorySlug: card.CategorySlug,
                brandName: null,
                primaryImageUrl: card.ImageUrl);

            await _productRepository.InsertAsync(product, autoSave: true);
        }

        product.ApplyListingSnapshot(
            title: card.Title,
            productUrl: card.ProductUrl,
            primaryCategorySlug: card.CategorySlug,
            brandName: product.BrandName,
            primaryImageUrl: card.ImageUrl,
            discountPercent: card.DiscountPercent,
            totalOfferCount: card.OfferCount,
            bestPriceAmount: card.BestPriceAmount,
            bestPriceMerchantName: card.BestMerchantName,
            previousPriceAmount: card.PreviousPriceAmount,
            utcNow: utcNow);

        var touchedMerchants = new HashSet<Guid>();
        var offersAdded = 0;

        if (includeOffers)
        {
            var detail = await _detailScraper.FetchAsync(card.ProductUrl, expandAllOffers, options);
            if (detail != null)
            {
                product.ApplyDetailSnapshot(
                    categoryPath: detail.CategoryPath,
                    brandName: detail.BrandName,
                    primaryImageUrl: detail.PrimaryImageUrl,
                    totalOfferCount: detail.TotalOfferCount ?? card.OfferCount,
                    utcNow: utcNow);

                product.ClearOffers();

                var offerCap = options.MaxOffersPerProduct > 0
                    ? options.MaxOffersPerProduct
                    : detail.Offers.Count;

                foreach (var offerExtract in detail.Offers.Take(offerCap))
                {
                    var merchant = await EnsureMerchantAsync(offerExtract, utcNow);
                    touchedMerchants.Add(merchant.Id);

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
                        offerUrl: offerExtract.OfferUrl);

                    product.AddOffer(offer);
                    offersAdded++;
                }
            }
            else
            {
                _logger.LogWarning(
                    "Cimri PDP HTML'i parse edilemedi — yalnızca listeleme alanları kaydedildi: {ProductUrl}",
                    card.ProductUrl);
            }
        }

        await _productRepository.UpdateAsync(product, autoSave: true);

        return (product.Id, offersAdded, touchedMerchants);
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
            if (!string.Equals(merchant.Name, offer.MerchantName.Trim(), StringComparison.Ordinal))
            {
                merchant.Rename(offer.MerchantName.Trim());
            }

            merchant.Touch(offer.MerchantLogoUrl, externalMerchantId: null, utcNow);
            await _merchantRepository.UpdateAsync(merchant, autoSave: true);
        }

        return merchant;
    }

    private static string BuildListingUrl(CimriClientOptions options)
    {
        var baseUrl = (options.BaseUrl ?? "https://www.cimri.com").TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(options.DiscountedListingPath)
            ? "/indirimli-urunler"
            : (options.DiscountedListingPath.StartsWith('/') ? options.DiscountedListingPath : "/" + options.DiscountedListingPath);
        return baseUrl + path;
    }

    private static int NormalizePositive(int? input, int defaultValue, int min, int max)
    {
        if (input is null || input.Value < min)
        {
            return Math.Clamp(defaultValue, min, max);
        }

        return Math.Clamp(input.Value, min, max);
    }
}
