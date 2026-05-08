using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BeeBAK.Marketplaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Trendyol;

public class TelegramListingSyncNotifier : IListingSyncNotifier, ITransientDependency
{
    public const string HttpClientName = "TelegramBot";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TrendyolClientOptions> _options;
    private readonly ILogger<TelegramListingSyncNotifier> _logger;

    public TelegramListingSyncNotifier(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TrendyolClientOptions> options,
        ILogger<TelegramListingSyncNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyListingSyncCompletedAsync(
        ListingSyncNotificationContext context,
        CancellationToken cancellationToken = default)
    {
        var telegram = _options.CurrentValue.Telegram;
        if (string.IsNullOrWhiteSpace(telegram.BotToken) || string.IsNullOrWhiteSpace(telegram.ChatId))
        {
            return;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url =
            $"https://api.telegram.org/bot{telegram.BotToken.Trim()}/sendMessage";

        var text =
            $"BeeBAK listing sync completed.{Environment.NewLine}" +
            $"Marketplace: {context.Marketplace}{Environment.NewLine}" +
            $"Query: {context.SearchQuery}{Environment.NewLine}" +
            $"Pages: {context.PagesFetched}{Environment.NewLine}" +
            $"Products affected: {context.ProductsAffected}{Environment.NewLine}" +
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
                _logger.LogWarning(
                    "Telegram notify failed: {Status} {Body}",
                    response.StatusCode,
                    body);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Telegram notify threw.");
        }
    }
}
