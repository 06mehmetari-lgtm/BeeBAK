using System;
using System.Collections.Generic;
using Volo.Abp.BackgroundJobs;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

/// <summary>
/// Birden fazla PDP işini tek RabbitMQ mesajında taşır — kuyruk uzunluğu ve iptal sonrası bekleyen mesaj sayısı azalır.
/// </summary>
[BackgroundJobName("cimri-product-detail-batch")]
public class CimriProductDetailBatchJobArgs
{
    public Guid ScrapeRunId { get; set; }

    /// <summary>İşlenecek ürün satırları; her biri <see cref="CimriProductDetailJob"/> ile sırayla işlenir.</summary>
    public List<CimriProductDetailJobArgs> Items { get; set; } = new();
}
