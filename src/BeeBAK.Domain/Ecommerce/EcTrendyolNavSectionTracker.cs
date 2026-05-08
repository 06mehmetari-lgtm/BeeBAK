using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BeeBAK.Ecommerce;

/// <summary>
/// Hangi Trendyol üst segment kodlarının alt navigasyondan senkronlanacağını tutar.
/// <see cref="ExternalCategoryId"/> örnekleri: butik liste için <c>"1"</c>, <c>"22"</c>; <c>"flas-indirimler"</c>;
/// Çok Satanlar linkindeki <c>webGenderId=1</c> için <c>"cok-satanlar:w1"</c>.
/// </summary>
public class EcTrendyolNavSectionTracker : FullAuditedAggregateRoot<Guid>
{
    public string ExternalCategoryId { get; protected set; } = default!;

    public bool IsActive { get; protected set; }

    public int SortOrder { get; protected set; }

    protected EcTrendyolNavSectionTracker()
    {
    }

    public EcTrendyolNavSectionTracker(Guid id, string externalCategoryId, int sortOrder = 0, bool isActive = true)
        : base(id)
    {
        ExternalCategoryId = Check.NotNullOrWhiteSpace(externalCategoryId, nameof(externalCategoryId));
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    public void SetActive(bool active) => IsActive = active;

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;
}
