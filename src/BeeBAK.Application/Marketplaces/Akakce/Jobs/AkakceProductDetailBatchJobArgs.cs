using System;
using System.Collections.Generic;
using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Akakce.Jobs;

[BackgroundJobName("akakce-product-detail-batch")]
public class AkakceProductDetailBatchJobArgs
{
    public Guid ScrapeRunId { get; set; }
    public List<AkakceProductDetailJobArgs> Items { get; set; } = new();
}
