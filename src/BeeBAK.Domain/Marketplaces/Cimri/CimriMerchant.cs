using System;
using BeeBAK.Marketplaces.Cimri;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>Cimri'de teklif sunan satıcı/mağaza referans kaydı.</summary>
public class CimriMerchant : FullAuditedAggregateRoot<Guid>
{
    /// <summary>Cimri sayfasında görünen mağaza adı (ör. "Hepsiburada", "PttAVM").</summary>
    public string Name { get; protected set; } = default!;

    /// <summary>Karşılaştırma anahtarı — küçük harf, boşluksuz; aynı mağazanın çoklu yazımlarını birleştirir.</summary>
    public string Slug { get; protected set; } = default!;

    /// <summary>Mağaza logosu (CDN).</summary>
    public string? LogoUrl { get; protected set; }

    /// <summary>Son seferinde gözüken Cimri merchant numerik kimliği — logodan/kbut'tan çıkarılırsa.</summary>
    public string? ExternalMerchantId { get; protected set; }

    public DateTime LastSeenUtc { get; protected set; }

    protected CimriMerchant()
    {
    }

    public CimriMerchant(Guid id, string name, string slug, DateTime utcNow)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: CimriConsts.MaxMerchantNameLength);
        Slug = Check.NotNullOrWhiteSpace(slug, nameof(slug), maxLength: CimriConsts.MaxMerchantSlugLength);
        LastSeenUtc = utcNow;
    }

    public void Touch(string? logoUrl, string? externalMerchantId, DateTime utcNow)
    {
        if (!string.IsNullOrWhiteSpace(logoUrl))
        {
            LogoUrl = logoUrl;
        }

        if (!string.IsNullOrWhiteSpace(externalMerchantId))
        {
            ExternalMerchantId = externalMerchantId;
        }

        LastSeenUtc = utcNow;
    }

    public void Rename(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: CimriConsts.MaxMerchantNameLength);
    }
}
