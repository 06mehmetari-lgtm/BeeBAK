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
    private readonly IAkakceTelegramProductCardSender _telegramProductCardSender;

    public AkakceProductIngestionService(
        IAkakceProductRepository productRepository,
        IAkakceMerchantRepository merchantRepository,
        AkakceProductDetailScraper detailScraper,
        IOptionsMonitor<AkakceClientOptions> options,
        ILogger<AkakceProductIngestionService> logger,
        IAkakceTelegramProductCardSender telegramProductCardSender)
    {
        _productRepository = productRepository;
        _merchantRepository = merchantRepository;
        _detailScraper = detailScraper;
        _options = options;
        _logger = logger;
        _telegramProductCardSender = telegramProductCardSender;
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
                await _telegramProductCardSender.TrySendAfterProductIngestedAsync(card.ProductCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Akakce Telegram kart paylaşımı başarısız: {ProductCode}", card.ProductCode);
            }
        }

        return new AkakceIngestionResult(product.Id, offersAdded, touchedMerchants, MarkVisitedInDedupCache: true);
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
