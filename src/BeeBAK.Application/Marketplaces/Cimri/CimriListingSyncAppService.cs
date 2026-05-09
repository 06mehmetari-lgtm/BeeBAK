using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces.Cimri.Jobs;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Cimri;

[Authorize(BeeBAKPermissions.Cimri.Sync)]
public class CimriListingSyncAppService : ApplicationService, ICimriListingSyncAppService
{
    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly CimriSeleniumPageFetcher _seleniumPageFetcher;
    private readonly CimriProductIngestionService _ingestionService;
    private readonly IListingSyncNotifier _listingSyncNotifier;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly ILogger<CimriListingSyncAppService> _logger;

    public CimriListingSyncAppService(
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IOptionsMonitor<CimriClientOptions> options,
        CimriSeleniumPageFetcher seleniumPageFetcher,
        CimriProductIngestionService ingestionService,
        IListingSyncNotifier listingSyncNotifier,
        IBackgroundJobManager backgroundJobManager,
        ILogger<CimriListingSyncAppService> logger)
    {
        _scrapeRunRepository = scrapeRunRepository;
        _options = options;
        _seleniumPageFetcher = seleniumPageFetcher;
        _ingestionService = ingestionService;
        _listingSyncNotifier = listingSyncNotifier;
        _backgroundJobManager = backgroundJobManager;
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

        if (options.UseQueue)
        {
            await _backgroundJobManager.EnqueueAsync(new CimriListingDiscoveryJobArgs
            {
                ScrapeRunId = scrapeRun.Id,
                MaxPages = maxPages,
                MaxProducts = maxProducts,
                IncludeOffers = includeOffers,
                ExpandAllOffers = expandAllOffers,
                ForceRefresh = effectiveInput.ForceRefresh,
            });

            _logger.LogInformation(
                "Cimri listing discovery queue'ya atıldı (scrapeRunId={RunId})",
                scrapeRun.Id);

            return new CimriListingSyncResultDto
            {
                ScrapeRunId = scrapeRun.Id,
                PagesFetched = 0,
                ProductsAffected = 0,
                OffersAffected = 0,
                MerchantsAffected = 0,
                ResolvedListingPageUrl = listingUrl,
                Queued = true,
            };
        }

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
                    var ingestion = await _ingestionService.UpsertAsync(card, includeOffers, expandAllOffers);

                    productsAffected++;
                    offersAffected += ingestion.OffersAdded;
                    foreach (var mid in ingestion.TouchedMerchantIds)
                    {
                        merchantsTouched.Add(mid);
                    }

                    if (options.DelayBetweenProductsMs > 0)
                    {
                        await Task.Delay(options.DelayBetweenProductsMs);
                    }
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
