using System;
using Volo.Abp.Domain.Entities;

namespace BeeBAK.Ecommerce;

/// <summary>
/// Scrape run sırasında oluşan tek bir adım/olay. UI bunları "konsol gibi" akan
/// canlı log paneli olarak gösterir. INSERT-only — yarış yok.
/// </summary>
public class EcScrapeRunEvent : Entity<Guid>
{
    public Guid ScrapeRunId { get; protected set; }

    public DateTime TimestampUtc { get; protected set; }

    public EcScrapeRunEventLevel Level { get; protected set; }

    /// <summary>"discovery", "detail", "system" vb.</summary>
    public string Phase { get; protected set; } = default!;

    public string Message { get; protected set; } = default!;

    public string? Title { get; protected set; }

    public string? Url { get; protected set; }

    /// <summary>X / total ilerleme yazımı için (opsiyonel).</summary>
    public int? Index { get; protected set; }

    public int? Total { get; protected set; }

    protected EcScrapeRunEvent()
    {
    }

    public EcScrapeRunEvent(
        Guid id,
        Guid scrapeRunId,
        DateTime timestampUtc,
        EcScrapeRunEventLevel level,
        string phase,
        string message,
        string? title = null,
        string? url = null,
        int? index = null,
        int? total = null)
        : base(id)
    {
        ScrapeRunId = scrapeRunId;
        TimestampUtc = timestampUtc;
        Level = level;
        Phase = phase;
        Message = message;
        Title = title;
        Url = url;
        Index = index;
        Total = total;
    }
}
