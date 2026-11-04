using System.Net.Http.Json;
using LpDashboard.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LpDashboard.Infrastructure;

public sealed class TelegramOptions
{
    public const string SectionName = "Alerts:Telegram";

    public string? BotToken { get; set; }
    public string? ChatId { get; set; }
}

/// <summary>Sends alerts through the Telegram Bot API (sendMessage).</summary>
public sealed class TelegramAlertNotifier(HttpClient http, IOptions<TelegramOptions> options, ILogger<TelegramAlertNotifier> logger)
    : IAlertNotifier
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(options.Value.BotToken) && !string.IsNullOrWhiteSpace(options.Value.ChatId);

    public async Task<string?> SendAsync(string message, CancellationToken ct)
    {
        if (!IsConfigured) return "Telegram bot token or chat id is not set";

        try
        {
            using var response = await http.PostAsJsonAsync(
                // Leading slash: without it the token's "id:secret" colon makes the relative URI parse as a scheme.
                $"/bot{options.Value.BotToken}/sendMessage",
                new { chat_id = options.Value.ChatId, text = message, disable_web_page_preview = true },
                ct);
            if (response.IsSuccessStatusCode) return null;

            // The body names the problem (bad chat id, blocked bot); the request URL, which holds the token, is never logged.
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Telegram rejected the alert: {Status} {Body}", (int)response.StatusCode, body);
            return $"Telegram returned {(int)response.StatusCode}: {body}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Could not reach Telegram: {Type}", ex.GetType().Name);
            return "Could not reach Telegram";
        }
    }
}
