using System;
using BeeBAK.Marketplaces;
using Volo.Abp.Application.Dtos;

namespace BeeBAK.Ecommerce;

public class EcMarketplaceProductDto : EntityDto<Guid>
{
    public MarketplaceKind Marketplace { get; set; }

    public string ExternalProductId { get; set; } = default!;

    public string Title { get; set; } = default!;

    public string ProductUrl { get; set; } = default!;

    public DateTime? LastSyncedUtc { get; set; }

    public decimal? LatestPriceAmount { get; set; }

    public string? Currency { get; set; }
}
