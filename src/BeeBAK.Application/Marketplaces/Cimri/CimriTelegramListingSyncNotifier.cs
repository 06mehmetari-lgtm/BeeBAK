using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Cimri;

public class CimriTelegramListingSyncNotifier : IListingSyncNotifier, ITransientDependency
{
    public const string HttpClientName = "CimriTelegramBot";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<CimriClientOptions> _options;
    private readonly ILogger<CimriTelegramListingSyncNotifier> _logger;

    public CimriTelegramListingSyncNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<CimriClientOptions> options,
        ILogger<CimriTelegramListingSyncNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyListingSyncCompletedAsync(
        ListingSyncNotificationContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Marketplace != MarketplaceKind.Cimri)
        {
            return;
        }

        var telegram = _options.CurrentValue.Telegram;
        if (string.IsNullOrWhiteSpace(telegram.BotToken) || string.IsNullOrWhiteSpace(telegram.ChatId))
        {
            return;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"https://api.telegram.org/bot{telegram.BotToken.Trim()}/sendMessage";

        var text =
            $"BeeBAK Cimri sync tamamlandı.{Environment.NewLine}" +
            $"Sayfa: {context.PagesFetched}{Environment.NewLine}" +
            $"Ürün etkilenen: {context.ProductsAffected}{Environment.NewLine}" +
            $"Scrape run: {context.ScrapeRunId}";

        try
        {
            using var response = await client.PostAsJsonAsync(
                url,
                new { chat_id = telegram.ChatId.Trim(), text },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Cimri Telegram notify failed: {Status} {Body}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cimri Telegram notify threw.");
        }
    }
}
