using System;
using System.Linq;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using BeeBAK.Marketplaces;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Marketplaces.Trendyol;

[Authorize(BeeBAKPermissions.Trendyol.SyncNavigationCatalog)]
public class TrendyolNavigationCatalogSyncAppService : ApplicationService, ITrendyolNavigationCatalogSyncAppService
{
    private readonly TrendyolNavigationHtmlClient _htmlClient;
    private readonly TrendyolBottomNavigationHtmlParser _parser;
    private readonly IRepository<EcTrendyolNavSectionTracker, Guid> _trackerRepository;
    private readonly IRepository<EcMarketplaceCategory, Guid> _categoryRepository;
    private readonly IOptionsMonitor<TrendyolClientOptions> _optionsMonitor;

    public TrendyolNavigationCatalogSyncAppService(
        TrendyolNavigationHtmlClient htmlClient,
        TrendyolBottomNavigationHtmlParser parser,
        IRepository<EcTrendyolNavSectionTracker, Guid> trackerRepository,
        IRepository<EcMarketplaceCategory, Guid> categoryRepository,
        IOptionsMonitor<TrendyolClientOptions> optionsMonitor)
    {
        _htmlClient = htmlClient;
        _parser = parser;
        _trackerRepository = trackerRepository;
        _categoryRepository = categoryRepository;
        _optionsMonitor = optionsMonitor;
    }

    public virtual async Task<TrendyolNavigationCatalogSyncResultDto> SyncRootSectionsFromNavigationAsync()
    {
        var html = await _htmlClient.GetNavigationHtmlAsync();
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new BusinessException("BeeBAK:TrendyolNavigationHtmlEmpty");
        }

        var parsedMap = _parser.ParseSectionItems(html);
        var trackers = await _trackerRepository.GetListAsync(x => x.IsActive);
        trackers = trackers.OrderBy(x => x.SortOrder).ThenBy(x => x.ExternalCategoryId).ToList();

        var dto = new TrendyolNavigationCatalogSyncResultDto
        {
            SectionAnchorsParsed = parsedMap.Count,
        };

        var siteOrigin = _optionsMonitor.CurrentValue.BaseUrl.TrimEnd('/');

        foreach (var tracker in trackers)
        {
            if (!parsedMap.TryGetValue(tracker.ExternalCategoryId, out var section))
            {
                dto.TrackersMissingInHtml++;
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(section.DisplayName)
                ? tracker.ExternalCategoryId
                : section.DisplayName;

            var fullUrl = $"{siteOrigin}{section.RelativeHref}";

            var existing = await _categoryRepository.FirstOrDefaultAsync(c =>
                c.Marketplace == MarketplaceKind.Trendyol &&
                c.ExternalCategoryId == tracker.ExternalCategoryId &&
                c.ParentId == null);

            var now = Clock.Now;

            if (existing == null)
            {
                var cat = new EcMarketplaceCategory(
                    GuidGenerator.Create(),
                    MarketplaceKind.Trendyol,
                    tracker.ExternalCategoryId,
                    displayName,
                    parentId: null,
                    slug: section.Slug,
                    categoryUrl: fullUrl,
                    extraAttributesJson: null);
                cat.ApplyTrendyolNavigationSnapshot(displayName, section.Slug, fullUrl, tracker.SortOrder, now);
                await _categoryRepository.InsertAsync(cat, autoSave: true);
                dto.CategoriesCreated++;
            }
            else
            {
                existing.ApplyTrendyolNavigationSnapshot(displayName, section.Slug, fullUrl, tracker.SortOrder, now);
                await _categoryRepository.UpdateAsync(existing, autoSave: true);
                dto.CategoriesUpdated++;
            }
        }

        return dto;
    }
}
