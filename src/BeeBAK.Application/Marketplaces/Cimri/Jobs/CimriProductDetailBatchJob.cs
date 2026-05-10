using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

/// <summary>
/// Toplu <see cref="CimriProductDetailJobArgs"/> — her ürün için mevcut detay job mantığını tekrar kullanır.
/// </summary>
public class CimriProductDetailBatchJob : AsyncBackgroundJob<CimriProductDetailBatchJobArgs>
{
    private readonly CimriProductDetailJob _detailJob;
    private readonly ILogger<CimriProductDetailBatchJob> _logger;

    public CimriProductDetailBatchJob(
        CimriProductDetailJob detailJob,
        ILogger<CimriProductDetailBatchJob> logger)
    {
        _detailJob = detailJob;
        _logger = logger;
    }

    public override async Task ExecuteAsync(CimriProductDetailBatchJobArgs args)
    {
        if (args.Items == null || args.Items.Count == 0)
        {
            return;
        }

        for (var i = 0; i < args.Items.Count; i++)
        {
            var item = args.Items[i];
            if (item == null)
            {
                continue;
            }

            if (item.ScrapeRunId == Guid.Empty && args.ScrapeRunId != Guid.Empty)
            {
                item.ScrapeRunId = args.ScrapeRunId;
            }

            try
            {
                await _detailJob.ExecuteAsync(item);
            }
            catch (Exception ex)
            {
                // Tek mesajda birden fazla ürün var; biri patlasa da diğerleri işlensin (ayrı kuyruk mesajlarındaki davranışa yakın).
                _logger.LogWarning(
                    ex,
                    "Cimri batch içinde ürün atlandı (runId={RunId}, index={Index}/{Total}, contentId={ContentId})",
                    args.ScrapeRunId, i + 1, args.Items.Count, item.ContentId);
            }
        }
    }
}
