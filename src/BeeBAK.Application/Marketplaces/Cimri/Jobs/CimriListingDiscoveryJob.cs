using System;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

/// <summary>
/// Cimri /indirimli-urunler sayfasını Selenium ile gezer, kart parser'ı ile ürün URL'lerini bulup
/// her birini ayrı bir <see cref="CimriProductDetailJobArgs"/> olarak queue'ya iter.
/// </summary>
public class CimriListingDiscoveryJob : AsyncBackgroundJob<CimriListingDiscoveryJobArgs>
{
    private readonly CimriSeleniumPageFetcher _seleniumPageFetcher;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly ICimriDedupCache _dedupCache;
    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<CimriListingDiscoveryJob> _ownLogger;

    public CimriListingDiscoveryJob(
        CimriSeleniumPageFetcher seleniumPageFetcher,
        IBackgroundJobManager backgroundJobManager,
        ICimriDedupCache dedupCache,
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IOptionsMonitor<CimriClientOptions> options,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<CimriListingDiscoveryJob> logger)
    {
        _seleniumPageFetcher = seleniumPageFetcher;
        _backgroundJobManager = backgroundJobManager;
        _dedupCache = dedupCache;
        _scrapeRunRepository = scrapeRunRepository;
        _options = options;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _ownLogger = logger;
    }

    public override async Task ExecuteAsync(CimriListingDiscoveryJobArgs args)
    {
        var options = _options.CurrentValue;
        var maxPages = Math.Clamp(args.MaxPages <= 0 ? options.DefaultMaxPages : args.MaxPages, 1, 200);
        var maxProducts = Math.Clamp(args.MaxProducts <= 0 ? options.DefaultMaxProducts : args.MaxProducts, 1, 5000);

        var listingUrl = BuildListingUrl(options);
        var loadMoreClicks = Math.Max(0, maxPages - 1);

        _ownLogger.LogInformation(
            "Cimri listing discovery starting (runId={RunId}, maxPages={Pages}, maxProducts={Products})",
            args.ScrapeRunId, maxPages, maxProducts);

        string? html;
        try
        {
            html = await _seleniumPageFetcher.TryGetListingHtmlAsync(listingUrl, loadMoreClicks, options);
        }
        catch (Exception ex)
        {
            _ownLogger.LogError(ex, "Cimri listing fetch failed (runId={RunId})", args.ScrapeRunId);
            await MarkRunFailedAsync(args.ScrapeRunId, ex.Message);
            throw;
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            await MarkRunFailedAsync(args.ScrapeRunId, "Empty listing HTML");
            throw new BusinessException("BeeBAK:CimriListingHtmlEmpty").WithData("ListingUrl", listingUrl);
        }

        var cards = CimriListingHtmlParser.Parse(html, options.BaseUrl).Take(maxProducts).ToList();
        _ownLogger.LogInformation("Cimri listing parsed {Count} cards", cards.Count);

        var enqueued = 0;
        foreach (var card in cards)
        {
            if (!args.ForceRefresh)
            {
                if (await _dedupCache.IsRecentlyVisitedAsync(card.ContentId))
                {
                    _ownLogger.LogDebug("Skip recently visited contentId={ContentId}", card.ContentId);
                    continue;
                }
            }

            await _backgroundJobManager.EnqueueAsync(new CimriProductDetailJobArgs
            {
                ScrapeRunId = args.ScrapeRunId,
                ContentId = card.ContentId,
                ProductUrl = card.ProductUrl,
                CategorySlug = card.CategorySlug,
                Title = card.Title,
                ImageUrl = card.ImageUrl,
                BestPriceAmount = card.BestPriceAmount,
                BestMerchantName = card.BestMerchantName,
                PreviousPriceAmount = card.PreviousPriceAmount,
                OfferCount = card.OfferCount,
                DiscountPercent = card.DiscountPercent.HasValue ? card.DiscountPercent : null,
                ExpandAllOffers = args.ExpandAllOffers,
                IncludeOffers = args.IncludeOffers,
                ForceRefresh = args.ForceRefresh,
            });

            enqueued++;
        }

        _ownLogger.LogInformation(
            "Cimri listing discovery enqueued {Enqueued}/{Total} product detail jobs (runId={RunId})",
            enqueued, cards.Count, args.ScrapeRunId);
    }

    private async Task MarkRunFailedAsync(Guid scrapeRunId, string message)
    {
        if (scrapeRunId == Guid.Empty)
        {
            return;
        }

        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true);
            var run = await _scrapeRunRepository.FindAsync(scrapeRunId);
            if (run != null)
            {
                run.Fail(_clock.Now, message);
                await _scrapeRunRepository.UpdateAsync(run, autoSave: true);
            }

            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            _ownLogger.LogTrace(ex, "ScrapeRun fail update başarısız: {RunId}", scrapeRunId);
        }
    }

    private static string BuildListingUrl(CimriClientOptions options)
    {
        var baseUrl = (options.BaseUrl ?? "https://www.cimri.com").TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(options.DiscountedListingPath)
            ? "/indirimli-urunler"
            : (options.DiscountedListingPath.StartsWith('/') ? options.DiscountedListingPath : "/" + options.DiscountedListingPath);
        return baseUrl + path;
    }
}
