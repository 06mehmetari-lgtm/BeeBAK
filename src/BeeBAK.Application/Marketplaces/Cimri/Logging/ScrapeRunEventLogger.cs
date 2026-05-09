using System;
using System.Threading.Tasks;
using BeeBAK.Ecommerce;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace BeeBAK.Marketplaces.Cimri.Logging;

/// <summary>
/// Scrape run boyunca oluşan adım/olayları DB'ye yazar. Her olay yeni satır (INSERT-only),
/// concurrency yarışı yok. UI bunları "konsol gibi" canlı akan log paneli olarak gösterir.
/// </summary>
public interface IScrapeRunEventLogger
{
    Task LogAsync(
        Guid scrapeRunId,
        EcScrapeRunEventLevel level,
        string phase,
        string message,
        string? title = null,
        string? url = null,
        int? index = null,
        int? total = null);
}

public class ScrapeRunEventLogger : IScrapeRunEventLogger, ITransientDependency
{
    private readonly IRepository<EcScrapeRunEvent, Guid> _eventRepository;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;
    private readonly ILogger<ScrapeRunEventLogger> _logger;

    public ScrapeRunEventLogger(
        IRepository<EcScrapeRunEvent, Guid> eventRepository,
        IUnitOfWorkManager uowManager,
        IGuidGenerator guidGenerator,
        IClock clock,
        ILogger<ScrapeRunEventLogger> logger)
    {
        _eventRepository = eventRepository;
        _uowManager = uowManager;
        _guidGenerator = guidGenerator;
        _clock = clock;
        _logger = logger;
    }

    public async Task LogAsync(
        Guid scrapeRunId,
        EcScrapeRunEventLevel level,
        string phase,
        string message,
        string? title = null,
        string? url = null,
        int? index = null,
        int? total = null)
    {
        if (scrapeRunId == Guid.Empty)
        {
            return;
        }

        try
        {
            using var uow = _uowManager.Begin(requiresNew: true);

            var entity = new EcScrapeRunEvent(
                _guidGenerator.Create(),
                scrapeRunId,
                _clock.Now.ToUniversalTime(),
                level,
                phase,
                Truncate(message, 1024),
                Truncate(title, 512),
                Truncate(url, 2048),
                index,
                total);

            await _eventRepository.InsertAsync(entity, autoSave: true);
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "ScrapeRunEvent yazılamadı (runId={RunId}, msg={Msg})", scrapeRunId, message);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
