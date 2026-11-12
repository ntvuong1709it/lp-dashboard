using System.Net;
using System.Text.Json;

namespace LpDashboard.Infrastructure.Risk;

/// <summary>Spaces requests to one source: at most <c>requestsPerMinute</c>, shared by every caller of the same instance.</summary>
internal sealed class RequestPacer(int requestsPerMinute)
{
    private readonly TimeSpan _interval = requestsPerMinute > 0 ? TimeSpan.FromSeconds(60.0 / requestsPerMinute) : TimeSpan.Zero;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _next = DateTime.MinValue;

    public async Task WaitAsync(CancellationToken ct)
    {
        if (_interval == TimeSpan.Zero) return;
        await _lock.WaitAsync(ct);
        try
        {
            var wait = _next - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _next = DateTime.UtcNow + _interval;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>A JSON body, or the reason there is none ("HTTP 429", "timeout", ...). Providers failing never throw.</summary>
internal sealed record JsonResponse(JsonDocument? Document, string? Failure) : IDisposable
{
    public void Dispose() => Document?.Dispose();
}

internal static class RiskHttp
{
    private const int MaxAttempts = 3;

    /// <summary>
    /// GET with pacing and a 429 retry (honouring Retry-After, otherwise 1 s then 2 s). Never throws for a provider failure
    /// and never puts the URL in a failure reason, since some URLs carry an API key.
    /// </summary>
    public static async Task<JsonResponse> GetJsonAsync(HttpClient http, RequestPacer pacer, string relativeUrl, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await pacer.WaitAsync(ct);
            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return new JsonResponse(null, "timeout");
            }
            catch (HttpRequestException)
            {
                return new JsonResponse(null, "network error");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt);
                    await Task.Delay(delay > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : delay, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode) return new JsonResponse(null, $"HTTP {(int)response.StatusCode}");

                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    return new JsonResponse(await JsonDocument.ParseAsync(stream, cancellationToken: ct), null);
                }
                catch (JsonException)
                {
                    return new JsonResponse(null, "unreadable response");
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    return new JsonResponse(null, "timeout");
                }
                catch (HttpRequestException)
                {
                    return new JsonResponse(null, "network error");
                }
            }
        }
    }

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        } : null;

    public static decimal? Dec(JsonElement e, string name) =>
        Str(e, name) is { Length: > 0 } s && decimal.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    public static int? Int(JsonElement e, string name) =>
        Str(e, name) is { Length: > 0 } s && int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : null;

    /// <summary>GoPlus-style flags: "1" is true, "0" is false, anything else (missing, empty) is unknown.</summary>
    public static bool? Flag(JsonElement e, string name) => Str(e, name) switch
    {
        "1" or "true" => true,
        "0" or "false" => false,
        _ => null,
    };
}
