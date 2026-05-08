using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Trendyol;

[Authorize(BeeBAKPermissions.Trendyol.Sync)]
public class TrendyolListingSyncAppService : ApplicationService, ITrendyolListingSyncAppService
{
    private readonly TrendyolSearchPayloadProvider _searchPayloadProvider;
    private readonly TrendyolSearchJsonParser _parser;
    private readonly IEcProductRepository _productRepository;
    private readonly IRepository<EcMarketplaceCategory, Guid> _categoryRepository;
    private readonly IRepository<EcScrapeRun, Guid> _scrapeRunRepository;
    private readonly IOptionsMonitor<TrendyolClientOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly IListingSyncNotifier _listingSyncNotifier;

    public TrendyolListingSyncAppService(
        TrendyolSearchPayloadProvider searchPayloadProvider,
        TrendyolSearchJsonParser parser,
        IEcProductRepository productRepository,
        IRepository<EcMarketplaceCategory, Guid> categoryRepository,
        IRepository<EcScrapeRun, Guid> scrapeRunRepository,
        IOptionsMonitor<TrendyolClientOptions> options,
        IMemoryCache cache,
        IListingSyncNotifier listingSyncNotifier)
    {
        _searchPayloadProvider = searchPayloadProvider;
        _parser = parser;
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _scrapeRunRepository = scrapeRunRepository;
        _options = options;
        _cache = cache;
        _listingSyncNotifier = listingSyncNotifier;
    }

    public virtual async Task<TrendyolListingSyncResultDto> SyncAsync(TrendyolListingSyncInput input)
    {
        var options = _options.CurrentValue;
        var resolvedQuery = await ResolveSearchQueryAsync(input);

        var maxPages = input.MaxPages ?? options.DefaultMaxPages;
        if (maxPages < 1)
        {
            throw new BusinessException("BeeBAK:TrendyolInvalidMaxPages")
                .WithData("MaxPages", maxPages);
        }

        var scrapeRun = new EcScrapeRun(
            GuidGenerator.Create(),
            MarketplaceKind.Trendyol,
            EcScrapeRunStatus.Running,
            Clock.Now,
            triggerSource: nameof(TrendyolListingSyncAppService));

        await _scrapeRunRepository.InsertAsync(scrapeRun, autoSave: true);

        Guid? primaryCategoryId = null;
        if (input.EcMarketplaceCategoryId is { } catId)
        {
            primaryCategoryId = catId;
        }

        var pagesFetched = 0;
        var productsAffected = 0;

        try
        {
            for (var page = 1; page <= maxPages; page++)
            {
                if (page > 1 && options.DelayBetweenRequestsMs > 0)
                {
                    await Task.Delay(options.DelayBetweenRequestsMs);
                }

                var json = await GetSearchPageJsonAsync(resolvedQuery, page, input.ForceRefresh, options);

                var items = _parser.Parse(json, options.BaseUrl.TrimEnd('/'));

                pagesFetched++;

                if (items.Count == 0)
                {
                    break;
                }

                foreach (var item in items)
                {
                    await UpsertProductAsync(item, primaryCategoryId);
                    productsAffected++;
                }
            }

            var statisticsJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["pagesFetched"] = pagesFetched,
                ["productsAffected"] = productsAffected,
                ["searchQuery"] = resolvedQuery,
                ["maxPagesConfigured"] = maxPages,
                ["completedUtc"] = Clock.Now.ToString("O")
            });

            scrapeRun.Complete(Clock.Now, statisticsJson);
            await _scrapeRunRepository.UpdateAsync(scrapeRun, autoSave: true);

            var dto = new TrendyolListingSyncResultDto
            {
                ScrapeRunId = scrapeRun.Id,
                PagesFetched = pagesFetched,
                ProductsAffected = productsAffected,
                ResolvedSearchQuery = resolvedQuery
            };

            await _listingSyncNotifier.NotifyListingSyncCompletedAsync(
                new ListingSyncNotificationContext(
                    MarketplaceKind.Trendyol,
                    scrapeRun.Id,
                    productsAffected,
                    pagesFetched,
                    resolvedQuery));

            return dto;
        }
        catch (BusinessException ex)
        {
            await FailScrapeRunAsync(scrapeRun, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            await FailScrapeRunAsync(scrapeRun, ex.Message);
            throw new BusinessException("BeeBAK:TrendyolSyncFailed")
                .WithData("Details", ex.Message);
        }
    }

    private async Task FailScrapeRunAsync(EcScrapeRun scrapeRun, string notes)
    {
        scrapeRun.Fail(Clock.Now, notes);
        await _scrapeRunRepository.UpdateAsync(scrapeRun, autoSave: true);
    }

    private async Task<string> ResolveSearchQueryAsync(TrendyolListingSyncInput input)
    {
        if (input.EcMarketplaceCategoryId is { } categoryId)
        {
            var category = await _categoryRepository.FindAsync(categoryId);
            if (category == null)
            {
                throw new BusinessException("BeeBAK:TrendyolCategoryNotFound")
                    .WithData("CategoryId", categoryId);
            }

            if (category.Marketplace != MarketplaceKind.Trendyol)
            {
                throw new BusinessException("BeeBAK:TrendyolCategoryWrongMarketplace")
                    .WithData("Marketplace", category.Marketplace.ToString());
            }

            if (!string.IsNullOrWhiteSpace(category.Name))
            {
                return category.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(category.Slug))
            {
                return category.Slug.Trim();
            }

            return category.ExternalCategoryId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(input.SearchQuery))
        {
            return input.SearchQuery.Trim();
        }

        throw new BusinessException("BeeBAK:TrendyolSearchQueryMissing");
    }

    private async Task<string> GetSearchPageJsonAsync(
        string query,
        int page,
        bool forceRefresh,
        TrendyolClientOptions options)
    {
        var cacheKey = BuildCacheKey(options, query, page);

        if (!forceRefresh &&
            options.CacheDurationSeconds > 0 &&
            _cache.TryGetValue(cacheKey, out string? cached) &&
            !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        string json;
        try
        {
            json = await _searchPayloadProvider.FetchSearchPayloadAsync(query, page);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BusinessException("BeeBAK:TrendyolHttpError")
                .WithData("Reason", ex.Message);
        }

        if (options.CacheDurationSeconds > 0)
        {
            _cache.Set(
                cacheKey,
                json,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.CacheDurationSeconds)
                });
        }

        return json;
    }

    private static string BuildCacheKey(TrendyolClientOptions options, string query, int page)
    {
        var defaults = "";
        if (options.DefaultQueryParameters is { Count: > 0 })
        {
            defaults = string.Join(
                "&",
                options.DefaultQueryParameters!
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Key}={kv.Value}"));
        }

        var fallbacks = "";
        if (options.FallbackSearchApiPaths is { Count: > 0 })
        {
            fallbacks = string.Join(
                ",",
                options.FallbackSearchApiPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        var raw =
            $"v4|html={options.EnableHtmlFallback}|{options.HtmlSearchPath}|{options.SearchApiPath}|{fallbacks}?{defaults}&q={query}&page={page}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "BeeBAK:Trendyol:SearchPage:" + Convert.ToHexString(hash);
    }

    /// <summary>
    /// Adds a new price snapshot when there is no prior snapshot or the selling price changed (see domain aggregates).
    /// </summary>
    private async Task UpsertProductAsync(TrendyolListingItem item, Guid? primaryCategoryId)
    {
        var existing = await _productRepository.FindByMarketplaceAndExternalIdAsync(
            MarketplaceKind.Trendyol,
            item.ExternalId,
            includePriceSnapshots: true);

        var now = Clock.Now;

        if (existing == null)
        {
            var productId = GuidGenerator.Create();
            var product = new EcProduct(
                productId,
                MarketplaceKind.Trendyol,
                item.ExternalId,
                item.Title,
                item.ProductUrl,
                primaryCategoryId,
                item.BrandName,
                barcode: null,
                item.MerchantExternalId);

            product.AddPriceSnapshot(
                new EcProductPriceSnapshot(
                    GuidGenerator.Create(),
                    productId,
                    item.Price,
                    "TRY",
                    now,
                    item.ListPrice,
                    item.DiscountPercent,
                    item.RawJson));

            product.TouchSync(now);
            await _productRepository.InsertAsync(product, autoSave: true);
            return;
        }

        existing.ApplyListingSync(
            item.Title,
            item.ProductUrl,
            primaryCategoryId,
            item.BrandName,
            item.MerchantExternalId);

        existing.TouchSync(now);

        var latest = existing.PriceSnapshots
            .OrderByDescending(s => s.ScrapedUtc)
            .FirstOrDefault();

        if (latest == null || latest.PriceAmount != item.Price)
        {
            existing.AddPriceSnapshot(
                new EcProductPriceSnapshot(
                    GuidGenerator.Create(),
                    existing.Id,
                    item.Price,
                    "TRY",
                    now,
                    item.ListPrice,
                    item.DiscountPercent,
                    item.RawJson));
        }

        await _productRepository.UpdateAsync(existing, autoSave: true);
    }
}
