using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Services;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Cimri ürün listeleme kartı + opsiyonel detay scrape sonucunu DB'ye işleyen merkezi servis.
/// Hem in-process listing sync, hem queue üzerinden çalışan detail job buradan geçer.
/// </summary>
public class CimriProductIngestionService : DomainService
{
    private readonly ICimriProductRepository _productRepository;
    private readonly ICimriMerchantRepository _merchantRepository;
    private readonly CimriProductDetailScraper _detailScraper;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly ILogger<CimriProductIngestionService> _logger;
    private readonly CimriTelegramPublishQueue _publishQueue;

    public CimriProductIngestionService(
        ICimriProductRepository productRepository,
        ICimriMerchantRepository merchantRepository,
        CimriProductDetailScraper detailScraper,
        IOptionsMonitor<CimriClientOptions> options,
        ILogger<CimriProductIngestionService> logger,
        CimriTelegramPublishQueue publishQueue)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _detailScraper = detailScraper;
        _options = options;
        _logger = logger;
        _publishQueue = publishQueue;
    }

    public async Task<CimriIngestionResult> UpsertAsync(
        CimriListingCard card,
        bool includeOffers,
        bool expandAllOffers,
        CimriRetailOfferRuntimePolicy retailPolicy,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var utcNow = Clock.Now;

        var existing = await _productRepository.FindByContentIdAsync(card.ContentId, includeOffers: true, cancellationToken);

        if (!includeOffers)
        {
            return await PersistListingSnapshotOnlyAsync(card, existing, utcNow, cancellationToken);
        }

        var strictRetention = retailPolicy.SkipProductIfNoQualifiedOffers;

        var detail = await _detailScraper.FetchAsync(card.ProductUrl, expandAllOffers, options, cancellationToken);

        if (detail == null)
        {
            _logger.LogWarning(
                "Cimri PDP HTML'i parse edilemedi — {ProductUrl}",
                card.ProductUrl);

            if (strictRetention && existing != null)
            {
                await _productRepository.DeleteAsync(existing, autoSave: true, cancellationToken: cancellationToken);
                return CimriIngestionResult.SkippedNoDedup();
            }

            if (strictRetention && existing == null)
            {
                return CimriIngestionResult.SkippedNoDedup();
            }

            return await PersistListingWithoutDetailAsync(card, existing, utcNow, cancellationToken);
        }

        var offerCap = options.MaxOffersPerProduct > 0
            ? options.MaxOffersPerProduct
            : int.MaxValue;

        var filteredOffers = FilterOffers(detail.Offers, retailPolicy, offerCap);

        if (strictRetention && filteredOffers.Count == 0)
        {
            if (existing != null)
            {
                await _productRepository.DeleteAsync(existing, autoSave: true, cancellationToken: cancellationToken);
            }

            return CimriIngestionResult.SkippedNoDedup();
        }

        // Güncellemeden önce önceki fiyat/indirim bilgisini sakla (trigger tespiti için)
        var prevBestPrice   = existing?.BestPriceAmount;
        var prevDiscountPct = existing?.DiscountPercent;

        var product = existing ?? new CimriProduct(
            GuidGenerator.Create(),
            card.ContentId,
            card.ProductUrl,
            card.Title,
            primaryCategorySlug: card.CategorySlug,
            brandName: null,
            primaryImageUrl: card.ImageUrl);

        if (existing == null)
        {
            await _productRepository.InsertAsync(product, autoSave: true, cancellationToken: cancellationToken);
        }

        product.ApplyListingSnapshot(
            title: card.Title,
            productUrl: card.ProductUrl,
            primaryCategorySlug: card.CategorySlug,
            brandName: product.BrandName,
            primaryImageUrl: card.ImageUrl,
            discountPercent: card.DiscountPercent,
            totalOfferCount: detail.TotalOfferCount ?? card.OfferCount,
            bestPriceAmount: card.BestPriceAmount,
            bestPriceMerchantName: card.BestMerchantName,
            previousPriceAmount: card.PreviousPriceAmount,
            utcNow: utcNow);

        product.ApplyDetailSnapshot(
            categoryPath: detail.CategoryPath,
            brandName: detail.BrandName,
            primaryImageUrl: detail.PrimaryImageUrl,
            totalOfferCount: detail.TotalOfferCount ?? card.OfferCount,
            utcNow: utcNow);

        product.ClearOffers();

        var touchedMerchants = new HashSet<Guid>();
        var offersAdded = 0;

        foreach (var offerExtract in filteredOffers)
        {
            var merchant = await EnsureMerchantAsync(offerExtract, utcNow, cancellationToken);
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
                offerUrl: offerExtract.OfferUrl,
                merchantProductUrl: offerExtract.MerchantProductUrl,
                merchantProductId: offerExtract.MerchantProductId);

            product.AddOffer(offer);
            offersAdded++;
        }

        await _productRepository.UpdateAsync(product, autoSave: true, cancellationToken: cancellationToken);

        if (offersAdded > 0)
        {
            try
            {
                var minDiscount = options.Publish.MinDiscountPercent;
                var currentDiscount = card.DiscountPercent ?? product.DiscountPercent;
                var currentPrice    = card.BestPriceAmount ?? product.BestPriceAmount ?? 0m;

                if (currentDiscount >= minDiscount || (prevBestPrice.HasValue && currentPrice < prevBestPrice.Value * 0.97m))
                {
                    var triggerType = DetermineTriggerType(
                        currentPrice, currentDiscount,
                        prevBestPrice, prevDiscountPct,
                        existing == null);

                    var score = CimriProductScorer.Calculate(currentDiscount, triggerType);

                    // detail.CategoryPath daha güvenilir (indirimli-urunler gibi çok kategorili
                    // sayfalarda card.CategorySlug boş gelir; detail PDP'den alınan gerçek kategoridir)
                    var effectiveCategorySlug = !string.IsNullOrEmpty(detail.CategoryPath)
                        ? detail.CategoryPath
                        : card.CategorySlug;

                    // Kitap kategorisi Telegram'a gönderilmez
                    if (TelegramCategoryFilter.IsBlocked(effectiveCategorySlug, card.Title))
                    {
                        _logger.LogDebug("Cimri: kitap/engelli kategori, kuyruklanmadı ({ContentId}, cat={Cat})",
                            card.ContentId, effectiveCategorySlug);
                    }
                    else
                    {
                        // En ucuz teklifin mağaza adı (mağaza çeşitliliği için)
                        var cheapestMerchant = filteredOffers
                            .OrderBy(o => o.Price)
                            .FirstOrDefault()?.MerchantName?.Trim()
                            ?? card.BestMerchantName?.Trim()
                            ?? "";

                        await _publishQueue.EnqueueAsync(new CimriPublishQueueEntry
                        {
                            ContentId       = card.ContentId,
                            Title           = card.Title,
                            TriggerType     = triggerType,
                            Score           = score,
                            LowestPrice     = currentPrice,
                            PreviousPrice   = prevBestPrice,
                            DiscountPercent = currentDiscount,
                            MerchantName    = cheapestMerchant,
                            CategorySlug    = effectiveCategorySlug,
                        }, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram yayın kuyruğuna eklenemedi: {ContentId}", card.ContentId);
            }
        }

        return new CimriIngestionResult(product.Id, offersAdded, touchedMerchants, MarkVisitedInDedupCache: true);
    }

    private static string DetermineTriggerType(
        decimal currentPrice,
        decimal? currentDiscount,
        decimal? prevPrice,
        decimal? prevDiscount,
        bool isNew)
    {
        if (isNew) return "new";

        if (prevPrice.HasValue && prevPrice.Value > 0m && currentPrice < prevPrice.Value * 0.97m)
            return "price_drop";

        if (prevDiscount.HasValue && currentDiscount.HasValue && currentDiscount.Value > prevDiscount.Value + 2m)
            return "discount_up";

        return "new";
    }

    private static List<CimriOfferExtract> FilterOffers(
        IReadOnlyList<CimriOfferExtract> offers,
        CimriRetailOfferRuntimePolicy policy,
        int offerCap)
    {
        IEnumerable<CimriOfferExtract> q = offers;
        if (policy.RestrictToAllowedMerchants)
        {
            q = q.Where(o => CimriMerchantNameMatcher.MatchesAnySubstring(
                o.MerchantName,
                policy.AllowedMerchantSubstrings));
        }

        if (policy.RequireMerchantProductId)
        {
            q = q.Where(o => !string.IsNullOrWhiteSpace(o.MerchantProductId));
        }

        return q.Take(offerCap).ToList();
    }

    private async Task<CimriIngestionResult> PersistListingSnapshotOnlyAsync(
        CimriListingCard card,
        CimriProduct? existing,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var product = existing ?? new CimriProduct(
            GuidGenerator.Create(),
            card.ContentId,
            card.ProductUrl,
            card.Title,
            primaryCategorySlug: card.CategorySlug,
            brandName: null,
            primaryImageUrl: card.ImageUrl);

        if (existing == null)
        {
            await _productRepository.InsertAsync(product, autoSave: true, cancellationToken: cancellationToken);
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

        await _productRepository.UpdateAsync(product, autoSave: true, cancellationToken: cancellationToken);

        return new CimriIngestionResult(product.Id, 0, new HashSet<Guid>(), MarkVisitedInDedupCache: true);
    }

    private async Task<CimriIngestionResult> PersistListingWithoutDetailAsync(
        CimriListingCard card,
        CimriProduct? existing,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var product = existing ?? new CimriProduct(
            GuidGenerator.Create(),
            card.ContentId,
            card.ProductUrl,
            card.Title,
            primaryCategorySlug: card.CategorySlug,
            brandName: null,
            primaryImageUrl: card.ImageUrl);

        if (existing == null)
        {
            await _productRepository.InsertAsync(product, autoSave: true, cancellationToken: cancellationToken);
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

        await _productRepository.UpdateAsync(product, autoSave: true, cancellationToken: cancellationToken);

        return new CimriIngestionResult(product.Id, 0, new HashSet<Guid>(), MarkVisitedInDedupCache: true);
    }

    public async Task<CimriMerchant> EnsureMerchantAsync(CimriOfferExtract offer, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var slug = CimriSlugifier.Slugify(offer.MerchantName);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = offer.MerchantName.Trim().ToLowerInvariant();
        }

        var merchant = await _merchantRepository.FindBySlugAsync(slug, cancellationToken);
        if (merchant == null)
        {
            merchant = new CimriMerchant(
                GuidGenerator.Create(),
                offer.MerchantName.Trim(),
                slug,
                utcNow);

            merchant.Touch(offer.MerchantLogoUrl, externalMerchantId: null, utcNow);
            await _merchantRepository.InsertAsync(merchant, autoSave: true, cancellationToken: cancellationToken);
        }
        else
        {
            if (!string.Equals(merchant.Name, offer.MerchantName.Trim(), StringComparison.Ordinal))
            {
                merchant.Rename(offer.MerchantName.Trim());
            }

            merchant.Touch(offer.MerchantLogoUrl, externalMerchantId: null, utcNow);
            await _merchantRepository.UpdateAsync(merchant, autoSave: true, cancellationToken: cancellationToken);
        }

        return merchant;
    }
}

public sealed record CimriIngestionResult(
    Guid ProductId,
    int OffersAdded,
    HashSet<Guid> TouchedMerchantIds,
    bool MarkVisitedInDedupCache)
{
    public static CimriIngestionResult SkippedNoDedup() =>
        new(Guid.Empty, 0, new HashSet<Guid>(), MarkVisitedInDedupCache: false);
}
