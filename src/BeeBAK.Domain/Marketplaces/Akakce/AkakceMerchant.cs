using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceMerchant : FullAuditedAggregateRoot<Guid>
{
    public string Name { get; protected set; } = default!;
    public string Slug { get; protected set; } = default!;
    public string? LogoUrl { get; protected set; }
    public DateTime LastSeenUtc { get; protected set; }

    protected AkakceMerchant()
    {
    }

    public AkakceMerchant(Guid id, string name, string slug, DateTime utcNow)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: AkakceConsts.MaxMerchantNameLength);
        Slug = Check.NotNullOrWhiteSpace(slug, nameof(slug), maxLength: AkakceConsts.MaxMerchantSlugLength);
        LastSeenUtc = utcNow;
    }

    public void Touch(string? logoUrl, DateTime utcNow)
    {
        if (!string.IsNullOrWhiteSpace(logoUrl))
        {
            LogoUrl = logoUrl;
        }

        LastSeenUtc = utcNow;
    }

    public void Rename(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: AkakceConsts.MaxMerchantNameLength);
    }
}
