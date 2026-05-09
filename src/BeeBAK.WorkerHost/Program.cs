using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Volo.Abp;

namespace BeeBAK;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Volo.Abp", LogEventLevel.Information)
            .MinimumLevel.Override("BeeBAK", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} | {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("Logs/worker.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            Log.Information("BeeBAK.WorkerHost başlatılıyor...");

            var hostBuilder = Host.CreateDefaultBuilder(args)
                .AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog();

            hostBuilder.ConfigureServices(async (_, services) =>
            {
                await services.AddApplicationAsync<BeeBAKWorkerHostModule>();
            });

            var host = hostBuilder.Build();
            await host.InitializeAsync();

            Log.Information(
                "BeeBAK.WorkerHost initialized — RabbitMQ tüketicileri çalışıyor. Process kapatılana kadar bekleniyor.");

            await host.RunAsync();
            return 0;
        }
        catch (System.Exception ex)
        {
            Log.Fatal(ex, "BeeBAK.WorkerHost başlatılırken kritik hata.");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
