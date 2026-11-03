namespace LpDashboard.Application.Abstractions;

/// <summary>Delivers a short text alert to the user (Telegram, ...). Implementations must not throw for delivery failures.</summary>
public interface IAlertNotifier
{
    bool IsConfigured { get; }

    /// <returns>Null on success, otherwise a human-readable reason it was not delivered.</returns>
    Task<string?> SendAsync(string message, CancellationToken ct);
}

/// <summary>Used when no channel is configured.</summary>
public sealed class NullAlertNotifier : IAlertNotifier
{
    public bool IsConfigured => false;

    public Task<string?> SendAsync(string message, CancellationToken ct) => Task.FromResult<string?>("No alert channel configured");
}
