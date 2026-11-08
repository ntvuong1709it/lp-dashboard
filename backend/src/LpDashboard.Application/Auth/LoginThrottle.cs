using System.Collections.Concurrent;

namespace LpDashboard.Application.Auth;

/// <summary>
/// Limits wrong-password attempts per client (IP): after <see cref="MaxFailures"/> failures within <see cref="Window"/> the client is
/// refused until the window passes. In memory, which is fine because the app runs as a single instance.
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset Start)> _clients = new();

    /// <summary>How long the client must wait, or null when it may try.</summary>
    public TimeSpan? RetryAfter(string client)
    {
        if (!_clients.TryGetValue(client, out var state)) return null;

        var now = time.GetUtcNow();
        if (now - state.Start >= Window)
        {
            _clients.TryRemove(client, out _);
            return null;
        }

        return state.Failures >= MaxFailures ? state.Start + Window - now : null;
    }

    public void RecordFailure(string client)
    {
        var now = time.GetUtcNow();
        _clients.AddOrUpdate(
            client,
            _ => (1, now),
            (_, s) => now - s.Start >= Window ? (1, now) : (s.Failures + 1, s.Start));
        Prune(now);
    }

    public void Reset(string client) => _clients.TryRemove(client, out _);

    private void Prune(DateTimeOffset now)
    {
        if (_clients.Count < 256) return;
        foreach (var (key, state) in _clients)
        {
            if (now - state.Start >= Window) _clients.TryRemove(key, out _);
        }
    }
}
