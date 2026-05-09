using System;
using System.Threading.Tasks;
using Medallion.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Uow;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

/// <summary>
/// Tek bir Cimri ürün PDP'sini çeken, mağaza redirect URL'lerini takip eden ve DB'ye yazan worker job'u.
/// Distributed lock + dedup cache ile aynı contentId'yi paralel iki worker'da işlenmesini engeller.
/// </summary>
public class CimriProductDetailJob : AsyncBackgroundJob<CimriProductDetailJobArgs>
{
    private readonly CimriProductIngestionService _ingestionService;
    private readonly ICimriDedupCache _dedupCache;
    private readonly IDistributedLockProvider? _lockProvider;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<CimriProductDetailJob> _ownLogger;

    public CimriProductDetailJob(
        CimriProductIngestionService ingestionService,
        ICimriDedupCache dedupCache,
        IOptionsMonitor<CimriClientOptions> options,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<CimriProductDetailJob> logger,
        IDistributedLockProvider? lockProvider = null)
    {
        _ingestionService = ingestionService;
        _dedupCache = dedupCache;
        _options = options;
        _unitOfWorkManager = unitOfWorkManager;
        _ownLogger = logger;
        _lockProvider = lockProvider;
    }

    public override async Task ExecuteAsync(CimriProductDetailJobArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.ContentId) || string.IsNullOrWhiteSpace(args.ProductUrl))
        {
            _ownLogger.LogWarning("Geçersiz job args (contentId/url boş)");
            return;
        }

        if (!args.ForceRefresh)
        {
            if (await _dedupCache.IsRecentlyVisitedAsync(args.ContentId))
            {
                _ownLogger.LogDebug("Cimri product detail skipped (recently visited): {ContentId}", args.ContentId);
                return;
            }
        }

        var lockKey = $"cimri:lock:{args.ContentId}";
        var handle = _lockProvider != null
            ? await _lockProvider.TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(30))
            : null;

        if (_lockProvider != null && handle == null)
        {
            _ownLogger.LogInformation("Cimri product detail lock alınamadı, başkası işliyor: {ContentId}", args.ContentId);
            return;
        }

        try
        {
            await ProcessAsync(args);

            await _dedupCache.TryAcquireAsync(
                args.ContentId,
                TimeSpan.FromSeconds(Math.Max(60, _options.CurrentValue.DedupTtlSeconds)));
        }
        finally
        {
            if (handle is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
            else if (handle is IDisposable syncDisposable)
            {
                syncDisposable.Dispose();
            }
        }
    }

    private async Task ProcessAsync(CimriProductDetailJobArgs args)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);

        var card = new CimriListingCard
        {
            ContentId = args.ContentId,
            ProductUrl = args.ProductUrl,
            Title = args.Title ?? string.Empty,
            CategorySlug = args.CategorySlug,
            ImageUrl = args.ImageUrl,
            BestPriceAmount = args.BestPriceAmount,
            BestMerchantName = args.BestMerchantName,
            PreviousPriceAmount = args.PreviousPriceAmount,
            OfferCount = args.OfferCount,
            DiscountPercent = args.DiscountPercent,
        };

        var result = await _ingestionService.UpsertAsync(card, args.IncludeOffers, args.ExpandAllOffers);
        await uow.CompleteAsync();

        _ownLogger.LogInformation(
            "Cimri product detail done: {ContentId} (productId={ProductId}, offers={Offers}, merchants={Merchants})",
            args.ContentId, result.ProductId, result.OffersAdded, result.TouchedMerchantIds.Count);
    }
}
