using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceProductDto : EntityDto<Guid>
{
    public string ProductCode { get; set; } = default!;
    public string ProductUrl { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? BrandName { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public string? CategoryPath { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? BestPriceAmount { get; set; }
    public decimal? PreviousPriceAmount { get; set; }
    public int? OfferCount { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastSyncedUtc { get; set; }
    public List<AkakceOfferDto> Offers { get; set; } = new();
}
