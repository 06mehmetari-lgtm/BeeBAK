using System;
using Volo.Abp.Domain.Entities;

namespace BeeBAK.Ecommerce;

public class EcProductImage : Entity<Guid>
{
    public Guid ProductId { get; protected set; }

    public string ImageUrl { get; protected set; } = default!;

    public int SortOrder { get; protected set; }

    public bool IsPrimary { get; protected set; }

    public virtual EcProduct Product { get; protected set; } = default!;

    protected EcProductImage()
    {
    }

    public EcProductImage(Guid id, Guid productId, string imageUrl, int sortOrder, bool isPrimary = false)
        : base(id)
    {
        ProductId = productId;
        ImageUrl = imageUrl;
        SortOrder = sortOrder;
        IsPrimary = isPrimary;
    }
}
