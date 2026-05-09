using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriProductDto : EntityDto<Guid>
{
    public string ContentId { get; set; } = default!;

    public string ProductUrl { get; set; } = default!;

    public string Title { get; set; } = default!;

    public string? PrimaryCategorySlug { get; set; }

    public string? CategoryPath { get; set; }

    public string? BrandName { get; set; }

    public string? PrimaryImageUrl { get; set; }

    public int? TotalOfferCount { get; set; }

    public decimal? DiscountPercent { get; set; }

    public decimal? BestPriceAmount { get; set; }

    public string? BestPriceMerchantName { get; set; }

    public decimal? PreviousPriceAmount { get; set; }

    public DateTime? LastSyncedUtc { get; set; }

    public List<CimriOfferDto> Offers { get; set; } = new();
}
