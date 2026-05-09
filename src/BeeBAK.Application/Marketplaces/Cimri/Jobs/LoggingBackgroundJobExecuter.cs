using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.MultiTenancy;

namespace BeeBAK.Marketplaces.Cimri.Jobs;

/// <summary>
/// ABP 10.3.0'da <see cref="Volo.Abp.BackgroundJobs.RabbitMQ.JobQueue{TArgs}.MessageReceived"/>
/// catch bloğu hiçbir log basmadan BasicReject ile mesajları silently dropluyor. Bu wrapper, Job
/// execute hatalarını ABP'nin internal Logger'ından bağımsız olarak bizim pipeline'ımızdan da
/// emin biçimde loglar — RabbitMQ pipeline'ında neyin patladığı her zaman görünür.
/// </summary>
public class LoggingBackgroundJobExecuter : BackgroundJobExecuter
{
    public ILogger<LoggingBackgroundJobExecuter> ExecLogger { get; set; }

    public LoggingBackgroundJobExecuter(IOptions<AbpBackgroundJobOptions> options, ICurrentTenant currentTenant)
        : base(options, currentTenant)
    {
        ExecLogger = NullLogger<LoggingBackgroundJobExecuter>.Instance;
    }

    public override async Task ExecuteAsync(JobExecutionContext context)
    {
        var jobName = context.JobType.Name;
        ExecLogger.LogDebug("BG job giriş: {Job} ({Args})", jobName, context.JobArgs?.GetType().Name);

        try
        {
            await base.ExecuteAsync(context);
            ExecLogger.LogDebug("BG job çıkış: {Job} (başarılı)", jobName);
        }
        catch (Exception ex)
        {
            ExecLogger.LogError(
                ex,
                "BG job patladı: {Job} — RabbitMQ tüketicisi mesajı reject edecek (requeue=BackgroundJobExecutionException ise true, diğerleri false)",
                jobName);
            throw;
        }
    }
}
