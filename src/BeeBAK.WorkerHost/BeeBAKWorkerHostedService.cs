using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Volo.Abp;

namespace BeeBAK;

/// <summary>
/// ABP application'ı ayağa kaldıran ve uygulama kapatılana kadar background workers'ı (RabbitMQ tüketicilerini)
/// canlı tutan hosted service. WorkerHost process'inin tamamı bu service'in StartAsync çağrısıyla başlar.
/// </summary>
public class BeeBAKWorkerHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BeeBAKWorkerHostedService> _logger;

    private IAbpApplicationWithInternalServiceProvider? _abpApplication;

    public BeeBAKWorkerHostedService(
        IHostApplicationLifetime lifetime,
        IConfiguration configuration,
        ILogger<BeeBAKWorkerHostedService> logger)
    {
        _lifetime = lifetime;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BeeBAK.WorkerHost başlatılıyor...");

        var application = await AbpApplicationFactory.CreateAsync<BeeBAKWorkerHostModule>(options =>
        {
            options.Services.ReplaceConfiguration(_configuration);
            options.UseAutofac();
            options.Services.AddLogging(c => c.AddSerilog());
        });

        await application.InitializeAsync();
        _abpApplication = application;

        _logger.LogInformation(
            "BeeBAK.WorkerHost initialized — RabbitMQ tüketicileri çalışıyor. Process kapatılana kadar bekleniyor.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BeeBAK.WorkerHost kapatılıyor...");

        if (_abpApplication != null)
        {
            await _abpApplication.ShutdownAsync();
            _abpApplication.Dispose();
            _abpApplication = null;
        }
    }
}
