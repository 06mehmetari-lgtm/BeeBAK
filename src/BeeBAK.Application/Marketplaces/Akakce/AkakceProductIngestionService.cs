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

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceProductIngestionService : DomainService
{
    private readonly IAkakceProductRepository _productRepository;
    private readonly IAkakceMerchantRepository _merchantRepository;
    private readonly AkakceProductDetailScraper _detailScraper;
    private readonly IOptionsMonitor<AkakceClientOptions> _options;
    private readonly ILogger<AkakceProductIngestionService> _logger;
    private readonly AkakceTelegramPublishQueue _publishQueue;

    public AkakceProductIngestionService(
        IAkakceProductRepository productRepository,
        IAkakceMerchantRepository merchantRepository,
        AkakceProductDetailScraper detailScraper,
        IOptionsMonitor<AkakceClientOptions> options,
        ILogger<AkakceProductIngestionService> logger,
        AkakceTelegramPublishQueue publishQueue)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _detailScraper = detailScraper;
        _options = options;
        _logger = logger;
        _publishQueue = publishQueue;
    }

    public async Task<AkakceIngestionResult> UpsertAsync(
        AkakceListingCard card,
        bool includeOffers,
        CancellationToken cancellationToken = default)
    {
        var utcNow = Clock.Now;
        var existing = await _productRepository.FindByProductCodeAsync(card.ProductCode, includeOffers: true, cancellationToken);
        if (!includeOffers)
        {
            return await PersistListingSnapshotOnlyAsync(card, existing, utcNow, cancellationToken);
        }

        var detail = await _detailScraper.FetchAsync(card.ProductCode, card.ProductUrl, _options.CurrentValue, cancellationToken);
        if (detail == null)
        {
            _logger.LogWarning("Akakce PDP parse failed: {ProductUrl}", card.ProductUrl);
            return await PersistListingSnapshotOnlyAsync(card, existing, utcNow, cancellationToken);
        }

        // Güncellemeden önce önceki fiyat/indirim bilgisini sakla (trigger tespiti için)
        var prevBestPrice   = existing?.BestPriceAmount;
        var prevDiscountPct = existing?.DiscountPercent;

        var product = existing ?? new AkakceProduct(
            GuidGenerator.Create(),
            card.ProductCode,
            card.ProductUrl,
            card.Title,
            card.BrandName,
            card.ImageUrl);

        if (existing == null)
        {
            await _productRepository.InsertAsync(product, autoSave: true, cancellationToken: cancellationToken);
        }

        product.ApplyListingSnapshot(
            card.Title,
            card.ProductUrl,
            card.BrandName,
            card.ImageUrl,
            card.DiscountPercent,
            detail.OfferCount ?? card.OfferCount,
            card.BestPriceAmount,
            card.PreviousPriceAmount,
            utcNow);

        product.ApplyDetailSnapshot(
            detail.CategoryPath,
            detail.BrandName,
            detail.PrimaryImageUrl,
            detail.OfferCount ?? card.OfferCount,
            utcNow);

        product.ClearOffers();

        var offerCap = _options.CurrentValue.MaxOffersPerProduct > 0
            ? _options.CurrentValue.MaxOffersPerProduct
            : int.MaxValue;

        var touchedMerchants = new HashSet<Guid>();
        var offersAdded = 0;
        foreach (var offerExtract in detail.Offers.Take(offerCap))
        {
            var merchant = await EnsureMerchantAsync(offerExtract, utcNow, cancellationToken);
            touchedMerchants.Add(merchant.Id);

            var offer = new AkakceOffer(
                GuidGenerator.Create(),
                product.Id,
                merchant.Id,
                offerExtract.Price,
                utcNow,
                offerExtract.DisplayOrder,
                offerExtract.Currency);

            offer.SetMetadata(
                offerExtract.OfferTitle,
                offerExtract.SellerName,
                offerExtract.ShippingText,
                offerExtract.ShippingAmount,
                offerExtract.IsFreeShipping,
                offerExtract.StockText,
                offerExtract.StockQuantity,
                offerExtract.DeliveryText,
                offerExtract.LastUpdatedText,
                offerExtract.LastUpdatedUtc,
                offerExtract.OfferUrl,
                offerExtract.MerchantProductUrl,
                offerExtract.SiteRedirectUrl);

            product.AddOffer(offer);
            offersAdded++;
        }

        await _productRepository.UpdateAsync(product, autoSave: true, cancellationToken: cancellationToken);

        if (offersAdded > 0)
        {
            try
            {
                var currentPrice    = card.BestPriceAmount ?? product.BestPriceAmount ?? 0m;
                var currentDiscount = card.DiscountPercent ?? product.DiscountPercent;

                var minDiscount = _options.CurrentValue.Publish.MinDiscountPercent;
                if (currentDiscount >= minDiscount
                    || (prevBestPrice.HasValue && currentPrice < prevBestPrice.Value * 0.97m))
                {
                    var triggerType = DetermineTriggerType(currentPrice, currentDiscount, prevBestPrice, prevDiscountPct, existing == null);
                    var score = ComputeScore(currentDiscount, triggerType);

                    // En ucuz teklifin mağaza adı (mağaza çeşitliliği için)
                    var cheapestMerchant = detail.Offers
                        .OrderBy(o => o.Price)
                        .FirstOrDefault()?.MerchantName?.Trim()
                        ?? "";

                    await _publishQueue.EnqueueAsync(new AkakcePublishQueueEntry
                    {
                        ProductCode     = card.ProductCode,
                        TriggerType     = triggerType,
                        Score           = score,
                        LowestPrice     = currentPrice,
                        PreviousPrice   = prevBestPrice,
                        DiscountPercent = currentDiscount,
                        MerchantName    = cheapestMerchant,
                    }, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Akakce yayın kuyruğuna eklenemedi: {ProductCode}", card.ProductCode);
            }
        }

        return new AkakceIngestionResult(product.Id, offersAdded, touchedMerchants, MarkVisitedInDedupCache: true);
    }

    private static string DetermineTriggerType(
        decimal currentPrice, decimal? currentDiscount,
        decimal? prevPrice, decimal? prevDiscount, bool isNew)
    {
        if (isNew) return "new";
        if (prevPrice.HasValue && prevPrice.Value > 0m && currentPrice < prevPrice.Value * 0.97m) return "price_drop";
        if (prevDiscount.HasValue && currentDiscount.HasValue && currentDiscount.Value > prevDiscount.Value + 2m) return "discount_up";
        return "new";
    }

    private static double ComputeScore(decimal? discountPercent, string triggerType)
    {
        double score = discountPercent >= 50m ? 100 : discountPercent >= 25m ? 70 : discountPercent >= 10m ? 40 : 10;
        score += triggerType switch { "price_drop" => 30, "discount_up" => 25, _ => 0 };
        return score;
    }

    private async Task<AkakceIngestionResult> PersistListingSnapshotOnlyAsync(
        AkakceListingCard card,
        AkakceProduct? existing,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var product = existing ?? new AkakceProduct(
            GuidGenerator.Create(),
            card.ProductCode,
            card.ProductUrl,
            card.Title,
            card.BrandName,
            card.ImageUrl);

        if (existing == null)
        {
            await _productRepository.InsertAsync(product, autoSave: true, cancellationToken: cancellationToken);
        }

        product.ApplyListingSnapshot(
            card.Title,
            card.ProductUrl,
            card.BrandName,
            card.ImageUrl,
            card.DiscountPercent,
            card.OfferCount,
            card.BestPriceAmount,
            card.PreviousPriceAmount,
            utcNow);

        await _productRepository.UpdateAsync(product, autoSave: true, cancellationToken: cancellationToken);
        return new AkakceIngestionResult(product.Id, 0, new HashSet<Guid>(), MarkVisitedInDedupCache: true);
    }

    private async Task<AkakceMerchant> EnsureMerchantAsync(
        AkakceOfferExtract offer,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var slug = AkakceSlugifier.Slugify(offer.MerchantName);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = offer.MerchantName.Trim().ToLowerInvariant();
        }

        var merchant = await _merchantRepository.FindBySlugAsync(slug, cancellationToken);
        if (merchant == null)
        {
            merchant = new AkakceMerchant(GuidGenerator.Create(), offer.MerchantName.Trim(), slug, utcNow);
            merchant.Touch(offer.MerchantLogoUrl, utcNow);
            await _merchantRepository.InsertAsync(merchant, autoSave: true, cancellationToken: cancellationToken);
        }
        else
        {
            if (!string.Equals(merchant.Name, offer.MerchantName.Trim(), StringComparison.Ordinal))
            {
                merchant.Rename(offer.MerchantName.Trim());
            }

            merchant.Touch(offer.MerchantLogoUrl, utcNow);
            await _merchantRepository.UpdateAsync(merchant, autoSave: true, cancellationToken: cancellationToken);
        }

        return merchant;
    }
}

public sealed record AkakceIngestionResult(
    Guid ProductId,
    int OffersAdded,
    HashSet<Guid> TouchedMerchantIds,
    bool MarkVisitedInDedupCache);
