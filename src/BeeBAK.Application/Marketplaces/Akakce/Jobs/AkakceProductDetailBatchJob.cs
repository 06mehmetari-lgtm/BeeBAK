using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Akakce.Jobs;

public class AkakceProductDetailBatchJob : AsyncBackgroundJob<AkakceProductDetailBatchJobArgs>
{
    private readonly AkakceProductDetailJob _detailJob;
    private readonly ILogger<AkakceProductDetailBatchJob> _logger;

    public AkakceProductDetailBatchJob(
        AkakceProductDetailJob detailJob,
        ILogger<AkakceProductDetailBatchJob> logger)
    {
        _detailJob = detailJob;
        _logger = logger;
    }

    public override async Task ExecuteAsync(AkakceProductDetailBatchJobArgs args)
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
                _logger.LogWarning(
                    ex,
                    "Akakce batch item skipped (runId={RunId}, index={Index}/{Total}, productCode={ProductCode})",
                    args.ScrapeRunId, i + 1, args.Items.Count, item.ProductCode);
            }
        }
    }
}
