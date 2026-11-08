using System.Security.Cryptography;
using LpDashboard.Application.Abstractions;
using LpDashboard.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LpDashboard.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Any value other than empty/"false" re-opens the setup page once (and revokes all sessions at startup).
    /// Change the value to reset again; a value left in the config does not re-open setup on later restarts.
    /// </summary>
    public string? ResetPassword { get; set; }
}

public enum AuthError
{
    None,
    SetupNotAllowed,
    SetupRequired,
    WeakPassword,
    InvalidPassword,
    Throttled,
}

public sealed record AuthOutcome(AuthError Error, string? Token = null, DateTime? ExpiresAt = null, TimeSpan? RetryAfter = null)
{
    public bool Succeeded => Error == AuthError.None;
}

public sealed record AuthStatus(bool SetupRequired, bool Authenticated);

/// <summary>Result of checking a cookie token; <see cref="RenewedUntil"/> is set when the sliding expiry moved and the cookie should be refreshed.</summary>
public sealed record SessionCheck(bool Valid, DateTime? RenewedUntil = null);

/// <summary>Single-account password login with revocable server-side sessions.</summary>
public sealed class AuthService(
    IAuthRepository auth,
    IUnitOfWork unitOfWork,
    LoginThrottle throttle,
    TimeProvider time,
    IOptions<AuthOptions> options)
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(365);
    public static readonly TimeSpan RenewAfter = TimeSpan.FromDays(1);

    private static readonly SemaphoreSlim SetupLock = new(1, 1);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>The configured reset value, or null when no reset is requested.</summary>
    private string? ResetValue => options.Value.ResetPassword is { } v && !string.IsNullOrWhiteSpace(v) && !v.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)
        ? v.Trim()
        : null;

    /// <summary>Setup is open when no password exists yet, or a reset has been requested and not yet used.</summary>
    private bool SetupAllowed(AuthCredential? credential) =>
        credential is null || (ResetValue is { } reset && credential.ResetNonce != reset);

    public async Task<AuthStatus> GetStatusAsync(string? token, CancellationToken ct)
    {
        var credential = await auth.GetCredentialAsync(ct);
        var setupRequired = SetupAllowed(credential);
        if (setupRequired || string.IsNullOrEmpty(token)) return new AuthStatus(setupRequired, false);

        return new AuthStatus(false, (await ValidateAsync(token, ct)).Valid);
    }

    /// <summary>True when a data request should answer "setup required" instead of "log in".</summary>
    public async Task<bool> IsSetupOpenAsync(CancellationToken ct) => SetupAllowed(await auth.GetCredentialAsync(ct));

    /// <summary>At startup: when a reset is requested, drop every session so the old password's devices are signed out.</summary>
    public async Task ApplyPendingResetAsync(CancellationToken ct)
    {
        var credential = await auth.GetCredentialAsync(ct);
        if (credential is not null && SetupAllowed(credential))
        {
            await auth.RemoveSessionsAsync(null, ct);
        }
    }

    public async Task<AuthOutcome> SetupAsync(string password, string? userAgent, CancellationToken ct)
    {
        if (!ValidLength(password)) return new AuthOutcome(AuthError.WeakPassword);

        await SetupLock.WaitAsync(ct);
        try
        {
            var credential = await auth.GetCredentialAsync(ct);
            if (!SetupAllowed(credential)) return new AuthOutcome(AuthError.SetupNotAllowed);

            var now = time.GetUtcNow().UtcDateTime;
            var hash = PasswordHasher.Hash(password);
            if (credential is null)
            {
                auth.Add(new AuthCredential { PasswordHash = hash, ResetNonce = ResetValue, CreatedAt = now, UpdatedAt = now });
            }
            else
            {
                credential.PasswordHash = hash;
                credential.ResetNonce = ResetValue;
                credential.UpdatedAt = now;
            }

            await auth.RemoveSessionsAsync(null, ct);
            var (session, token) = NewSession(now, userAgent);
            auth.Add(session);
            await unitOfWork.SaveChangesAsync(ct);
            return new AuthOutcome(AuthError.None, token, session.ExpiresAt);
        }
        finally
        {
            SetupLock.Release();
        }
    }

    public async Task<AuthOutcome> LoginAsync(string password, string client, string? userAgent, CancellationToken ct)
    {
        if (throttle.RetryAfter(client) is { } wait) return new AuthOutcome(AuthError.Throttled, RetryAfter: wait);

        var credential = await auth.GetCredentialAsync(ct);
        if (credential is null || SetupAllowed(credential)) return new AuthOutcome(AuthError.SetupRequired);

        // Bound the work an attacker can make us do with an oversized password.
        if (password.Length > MaxPasswordLength || !PasswordHasher.Verify(password, credential.PasswordHash))
        {
            return await FailAsync(client, ct);
        }

        throttle.Reset(client);
        var now = time.GetUtcNow().UtcDateTime;
        await auth.RemoveExpiredSessionsAsync(now, ct);
        var (session, token) = NewSession(now, userAgent);
        auth.Add(session);
        await unitOfWork.SaveChangesAsync(ct);
        return new AuthOutcome(AuthError.None, token, session.ExpiresAt);
    }

    public async Task<SessionCheck> ValidateAsync(string token, CancellationToken ct)
    {
        var session = await auth.FindSessionAsync(HashToken(token), ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (session is null || session.ExpiresAt <= now) return new SessionCheck(false);

        if (now - session.LastSeenAt < RenewAfter) return new SessionCheck(true);

        session.LastSeenAt = now;
        session.ExpiresAt = now + SessionLifetime;
        await unitOfWork.SaveChangesAsync(ct);
        return new SessionCheck(true, session.ExpiresAt);
    }

    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        if (await auth.FindSessionAsync(HashToken(token), ct) is { } session)
        {
            auth.Remove(session);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public Task LogoutEverywhereAsync(CancellationToken ct) => auth.RemoveSessionsAsync(null, ct);

    /// <summary>Changes the password and signs out every other device (the caller's session stays).</summary>
    public async Task<AuthError> ChangePasswordAsync(string current, string next, string token, string client, CancellationToken ct)
    {
        if (throttle.RetryAfter(client) is not null) return AuthError.Throttled;

        var credential = await auth.GetCredentialAsync(ct);
        if (credential is null) return AuthError.SetupRequired;

        if (current.Length > MaxPasswordLength || !PasswordHasher.Verify(current, credential.PasswordHash))
        {
            await FailAsync(client, ct);
            return AuthError.InvalidPassword;
        }

        if (!ValidLength(next)) return AuthError.WeakPassword;

        throttle.Reset(client);
        credential.PasswordHash = PasswordHasher.Hash(next);
        credential.UpdatedAt = time.GetUtcNow().UtcDateTime;
        var keep = (await auth.FindSessionAsync(HashToken(token), ct))?.Id;
        await unitOfWork.SaveChangesAsync(ct);
        await auth.RemoveSessionsAsync(keep, ct);
        return AuthError.None;
    }

    private async Task<AuthOutcome> FailAsync(string client, CancellationToken ct)
    {
        throttle.RecordFailure(client);
        await Task.Delay(FailureDelay, ct);
        return new AuthOutcome(AuthError.InvalidPassword);
    }

    private static bool ValidLength(string password) => password.Length is >= MinPasswordLength and <= MaxPasswordLength;

    private static (AuthSession Session, string Token) NewSession(DateTime now, string? userAgent)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var session = new AuthSession
        {
            TokenHash = HashToken(token),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + SessionLifetime,
            UserAgent = userAgent is { Length: > 200 } ? userAgent[..200] : userAgent,
        };
        return (session, token);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
