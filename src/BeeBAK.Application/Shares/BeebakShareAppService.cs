using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace BeeBAK.Shares;

[Authorize(BeeBAKPermissions.Cimri.Default)]
public class BeebakShareAppService : ApplicationService, IBeebakShareAppService
{
    private static readonly string[] Themes = ["ember", "aurora", "tide", "citrus"];

    private static readonly string[] Headlines =
    [
        "Muhteşem Fırsat",
        "Bugün Kaçırma",
        "Süper İndirim",
        "Fırsat Köşesi",
        "Sepete İndirim",
        "Kaçırılmayacak Fiyat",
        "Akıllı Alışveriş",
        "En İyi Teklifler",
        "İndirim Zamanı",
        "Şahane Fiyat",
        "Favori Ürünler",
        "Liste Özel",
        "Tavan İndirim",
        "Hızlı Fırsat",
        "Bugünün Yıldızları",
        "Tıkla Kazan",
        "Net Kazanç",
        "Uçuran İndirim",
        "Alt Üst Fiyat",
        "Sepet Dostu",
        "Kupon Gibi",
        "Ramazan Fırsatı",
        "Bayram Öncesi",
        "Stok Bitmeden",
        "Son Kale",
        "Torbaya İndirim",
        "Marka Şov",
        "Rakipsiz Fiyat",
        "Mağaza Yarışı",
        "Üç Mağaza Üç Link",
    ];

    private static readonly string[] Taglines =
    [
        "beebaksana ile en ucuz üç adres.",
        "Ürünleri numaralandırdık — linkler sırayla uyuyor.",
        "Tıkla, karşılaştır, kazan.",
        "Fiyatlar anlık — acele et.",
        "Üç mağaza, tek bakışta.",
        "Senin için süzdük, en düşük üçü yazdık.",
        "Paylaş — arkadaşın da görsün.",
        "İndirim yüzdesi kapakta, linkler altta.",
        "Bugün seçtik, yarın yenileri gelir.",
        "Kaliteli ürün, net fiyat.",
    ];

    private readonly ICimriProductRepository _cimriProductRepository;
    private readonly IRepository<CimriMerchant, Guid> _cimriMerchantRepository;
    private readonly IRepository<BeebakShareProductDayBlock, Guid> _dayBlockRepository;
    private readonly IRepository<BeebakShareCardLog, Guid> _cardLogRepository;

    public BeebakShareAppService(
        ICimriProductRepository cimriProductRepository,
        IRepository<CimriMerchant, Guid> cimriMerchantRepository,
        IRepository<BeebakShareProductDayBlock, Guid> dayBlockRepository,
        IRepository<BeebakShareCardLog, Guid> cardLogRepository)
    {
        _cimriProductRepository = cimriProductRepository;
        _cimriMerchantRepository = cimriMerchantRepository;
        _dayBlockRepository = dayBlockRepository;
        _cardLogRepository = cardLogRepository;
    }

    public virtual async Task<BeebakShareDeckDto> BuildDeckAsync(ShareDeckBuildInput input)
    {
        var channel = string.IsNullOrWhiteSpace(input.ChannelName)
            ? BeebakShareConsts.DefaultChannelName
            : input.ChannelName.Trim();

        var maxSlots = Math.Clamp(input.MaxSlotsPerCard, 1, 4);
        var maxTotal = Math.Clamp(input.MaxProductsTotal, 1, 96);

        var utcDay = Clock.Now.ToUniversalTime().Date;

        var blockedRows = await _dayBlockRepository.GetListAsync(x =>
            x.BlockUtcDate == utcDay && x.ChannelName == channel);

        var exclude = blockedRows.Select(x => x.CimriContentId).ToHashSet();

        var candidates = await _cimriProductRepository.GetShareDeckCandidatesAsync(
            take: maxTotal * 2,
            excludeContentIds: exclude);

        var trimmed = candidates.Take(maxTotal).ToList();

        var merchantIds = trimmed.SelectMany(p => p.Offers.Select(o => o.MerchantId)).Distinct().ToList();
        var merchants = merchantIds.Count == 0
            ? new List<CimriMerchant>()
            : await _cimriMerchantRepository.GetListAsync(m => merchantIds.Contains(m.Id));
        var merchantNames = merchants.ToDictionary(m => m.Id, m => m.Name);

        var chunks = Chunk(trimmed, maxSlots);
        var deck = new BeebakShareDeckDto
        {
            GeneratedUtc = Clock.Now.ToUniversalTime(),
            CandidatePoolCount = trimmed.Count,
            SkippedAlreadySharedTodayCount = exclude.Count,
            Cards = new List<BeebakShareCardDto>(),
        };

        var rng = new Random(HashCode.Combine(utcDay, channel.GetHashCode()));

        var cardIndex = 0;
        foreach (var chunk in chunks)
        {
            if (chunk.Count == 0)
            {
                continue;
            }

            var theme = Themes[cardIndex % Themes.Length];
            var headline = Headlines[rng.Next(Headlines.Length)];
            var tagline = Taglines[rng.Next(Taglines.Length)];

            var card = new BeebakShareCardDto
            {
                CardId = GuidGenerator.Create(),
                ChannelName = channel,
                Theme = theme,
                Headline = headline,
                Tagline = tagline,
                Slots = new List<BeebakShareCardSlotDto>(),
            };

            var slotNumber = 1;
            foreach (var product in chunk)
            {
                card.Slots.Add(MapSlot(product, slotNumber, merchantNames));
                slotNumber++;
            }

            deck.Cards.Add(card);
            cardIndex++;
        }

        return deck;
    }

    public virtual async Task RecordShareAsync(RecordShareInput input)
    {
        var channel = string.IsNullOrWhiteSpace(input.ChannelName)
            ? BeebakShareConsts.DefaultChannelName
            : input.ChannelName.Trim();

        if (input.CimriContentIds == null || input.CimriContentIds.Count == 0)
        {
            throw new UserFriendlyException(L["BeebakShareEmptyCard"]);
        }

        var fingerprint = BuildFingerprint(input.CimriContentIds);
        var utcDay = Clock.Now.ToUniversalTime().Date;
        var utcDayEnd = utcDay.AddDays(1);

        var duplicateToday = await _cardLogRepository.AnyAsync(x =>
            x.ProductFingerprint == fingerprint
            && x.ChannelName == channel
            && x.CreatedUtc >= utcDay && x.CreatedUtc < utcDayEnd);

        if (duplicateToday)
        {
            throw new UserFriendlyException(L["BeebakShareDuplicateToday"]);
        }

        var payloadJson = string.IsNullOrWhiteSpace(input.CardPayloadJson)
            ? "{}"
            : input.CardPayloadJson!;

        await _cardLogRepository.InsertAsync(
            new BeebakShareCardLog(
                GuidGenerator.Create(),
                Clock.Now.ToUniversalTime(),
                channel,
                payloadJson,
                fingerprint),
            autoSave: true);

        foreach (var contentId in input.CimriContentIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            var exists = await _dayBlockRepository.AnyAsync(x =>
                x.CimriContentId == contentId.Trim()
                && x.BlockUtcDate == utcDay
                && x.ChannelName == channel);

            if (exists)
            {
                continue;
            }

            await _dayBlockRepository.InsertAsync(
                new BeebakShareProductDayBlock(
                    GuidGenerator.Create(),
                    contentId.Trim(),
                    utcDay,
                    channel,
                    Clock.Now.ToUniversalTime()),
                autoSave: true);
        }
    }

    public virtual async Task<PagedResultDto<ShareHistoryItemDto>> GetHistoryAsync(PagedAndSortedResultRequestDto input)
    {
        var query = await _cardLogRepository.GetQueryableAsync();
        var q = query.OrderByDescending(x => x.CreatedUtc);

        var total = await AsyncExecuter.CountAsync(q);
        var list = await AsyncExecuter.ToListAsync(
            q.Skip(input.SkipCount).Take(input.MaxResultCount));

        var items = list.Select(x => new ShareHistoryItemDto
        {
            Id = x.Id,
            CreatedUtc = x.CreatedUtc,
            ChannelName = x.ChannelName,
            ProductFingerprint = x.ProductFingerprint,
            SummaryLine = TrySummarize(x.CardPayloadJson),
        }).ToList();

        return new PagedResultDto<ShareHistoryItemDto>(total, items);
    }

    private static string? TrySummarize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("headline", out var h))
            {
                return h.GetString();
            }

            if (doc.RootElement.TryGetProperty("Headline", out var h2))
            {
                return h2.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private BeebakShareCardSlotDto MapSlot(
        CimriProduct product,
        int slotIndex,
        IReadOnlyDictionary<Guid, string> merchantNames)
    {
        var links = SelectTopMerchantLinks(product, 3, merchantNames);
        return new BeebakShareCardSlotDto
        {
            SlotIndex = slotIndex,
            ContentId = product.ContentId,
            Title = product.Title,
            ImageUrl = product.PrimaryImageUrl,
            DiscountPercent = product.DiscountPercent,
            TopMerchantLinks = links,
        };
    }

    private static List<BeebakShareLinkDto> SelectTopMerchantLinks(
        CimriProduct product,
        int max,
        IReadOnlyDictionary<Guid, string> merchantNames)
    {
        var ordered = product.Offers
            .OrderBy(o => o.Price)
            .GroupBy(o => o.MerchantId)
            .Select(g => g.First())
            .Take(max)
            .ToList();

        var list = new List<BeebakShareLinkDto>();
        foreach (var o in ordered)
        {
            var url = !string.IsNullOrWhiteSpace(o.MerchantProductUrl)
                ? o.MerchantProductUrl!
                : o.OfferUrl ?? product.ProductUrl;

            merchantNames.TryGetValue(o.MerchantId, out var storeName);
            var label = !string.IsNullOrWhiteSpace(storeName)
                ? storeName
                : (o.OfferTitle ?? o.SellerName ?? "Mağaza");

            list.Add(new BeebakShareLinkDto
            {
                MerchantName = label,
                Price = o.Price,
                Currency = o.Currency,
                Url = url,
            });
        }

        return list;
    }

    private static List<List<CimriProduct>> Chunk(List<CimriProduct> items, int maxPerCard)
    {
        var chunks = new List<List<CimriProduct>>();
        for (var i = 0; i < items.Count;)
        {
            var n = Math.Min(maxPerCard, items.Count - i);
            chunks.Add(items.GetRange(i, n));
            i += n;
        }

        return chunks;
    }

    private static string BuildFingerprint(IEnumerable<string> contentIds)
    {
        var sorted = contentIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var fp = string.Join("|", sorted);
        if (fp.Length > BeebakShareConsts.MaxFingerprintLength)
        {
            throw new BusinessException("BeeBAK:BeebakShareFingerprintTooLong");
        }

        return fp;
    }
}
