using LpDashboard.Application;
using LpDashboard.Application.Auth;
using LpDashboard.Infrastructure;
using LpDashboard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LpDashboard.Domain.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_AcceptsOnlyTheSamePassword()
    {
        var stored = PasswordHasher.Hash("correct horse", iterations: 1_000);

        Assert.True(PasswordHasher.Verify("correct horse", stored));
        Assert.False(PasswordHasher.Verify("correct horse ", stored));
        Assert.False(PasswordHasher.Verify("", stored));
    }

    [Fact]
    public void Hash_UsesAFreshSaltEachTime()
    {
        Assert.NotEqual(PasswordHasher.Hash("same password", 1_000), PasswordHasher.Hash("same password", 1_000));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("PBKDF2$sha256$x$AAAA$AAAA")]
    [InlineData("PBKDF2$sha256$1000$not-base64!$AAAA")]
    public void Verify_RejectsMalformedHashes(string stored)
    {
        Assert.False(PasswordHasher.Verify("anything", stored));
    }
}

public sealed class LoginThrottleTests
{
    private readonly TestClock _clock = new(new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void BlocksAfterMaxFailures_ThenRecoversWhenTheWindowPasses()
    {
        var throttle = new LoginThrottle(_clock);
        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++) throttle.RecordFailure("1.2.3.4");
        Assert.Null(throttle.RetryAfter("1.2.3.4"));

        throttle.RecordFailure("1.2.3.4");
        Assert.Equal(LoginThrottle.Window, throttle.RetryAfter("1.2.3.4"));

        _clock.Advance(LoginThrottle.Window);
        Assert.Null(throttle.RetryAfter("1.2.3.4"));
    }

    [Fact]
    public void TracksClientsSeparately_AndSuccessClearsTheCount()
    {
        var throttle = new LoginThrottle(_clock);
        for (var i = 0; i < LoginThrottle.MaxFailures; i++) throttle.RecordFailure("a");

        Assert.NotNull(throttle.RetryAfter("a"));
        Assert.Null(throttle.RetryAfter("b"));

        throttle.Reset("a");
        Assert.Null(throttle.RetryAfter("a"));
    }
}

/// <summary>Real auth service and repositories over a throwaway SQLite file, with a controllable clock.</summary>
public sealed class AuthServiceTests : IDisposable
{
    private const string Password = "a long enough password";
    private const string Client = "10.0.0.1";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lpdash-auth-{Guid.NewGuid():N}.db");
    private readonly TestClock _clock = new(new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc));
    private readonly LoginThrottle _throttle;
    private readonly List<ServiceProvider> _providers = [];

    public AuthServiceTests()
    {
        _throttle = new LoginThrottle(_clock);
        using var scope = Open();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task BeforeSetup_StatusAsksForSetup_AndLoginIsRefused()
    {
        using var scope = Open();
        var auth = Auth(scope);

        Assert.Equal(new AuthStatus(SetupRequired: true, Authenticated: false), await auth.GetStatusAsync(null, default));
        Assert.True(await auth.IsSetupOpenAsync(default));
        Assert.Equal(AuthError.SetupRequired, (await auth.LoginAsync(Password, Client, null, default)).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    public async Task Setup_RejectsPasswordsShorterThanEightCharacters(string password)
    {
        using var scope = Open();

        var outcome = await Auth(scope).SetupAsync(password, null, default);

        Assert.Equal(AuthError.WeakPassword, outcome.Error);
        Assert.True(await Auth(scope).IsSetupOpenAsync(default));
    }

    [Fact]
    public async Task Setup_AcceptsEightCharacters_SignsIn_AndClosesSetup()
    {
        using var scope = Open();
        var auth = Auth(scope);

        var outcome = await auth.SetupAsync("12345678", "Test UA", default);

        Assert.True(outcome.Succeeded);
        Assert.False(string.IsNullOrEmpty(outcome.Token));
        Assert.Equal(new AuthStatus(false, true), await auth.GetStatusAsync(outcome.Token, default));
        Assert.Equal(AuthError.SetupNotAllowed, (await auth.SetupAsync("another password", null, default)).Error);
    }

    [Fact]
    public async Task Setup_DoesNotStoreThePasswordOrTheRawToken()
    {
        using var scope = Open();
        var outcome = await Auth(scope).SetupAsync(Password, null, default);

        await using var db = NewDb();
        var credential = await db.AuthCredentials.SingleAsync();
        var session = await db.AuthSessions.SingleAsync();
        Assert.DoesNotContain(Password, credential.PasswordHash);
        Assert.NotEqual(outcome.Token, session.TokenHash);
        Assert.Equal(64, session.TokenHash.Length);
    }

    [Fact]
    public async Task Login_AcceptsTheRightPassword_AndRejectsAWrongOne()
    {
        await SetUpAsync();
        using var scope = Open();
        var auth = Auth(scope);

        var ok = await auth.LoginAsync(Password, Client, "Phone", default);
        var wrong = await auth.LoginAsync("not the password", Client, "Phone", default);

        Assert.True(ok.Succeeded);
        Assert.Equal(AuthError.InvalidPassword, wrong.Error);
        Assert.True((await auth.ValidateAsync(ok.Token!, default)).Valid);
    }

    [Fact]
    public async Task Login_IsRefusedAfterTooManyWrongAttempts_EvenWithTheRightPassword()
    {
        await SetUpAsync();
        for (var i = 0; i < LoginThrottle.MaxFailures; i++) _throttle.RecordFailure(Client);
        using var scope = Open();

        var outcome = await Auth(scope).LoginAsync(Password, Client, null, default);

        Assert.Equal(AuthError.Throttled, outcome.Error);
        Assert.NotNull(outcome.RetryAfter);

        // Another client is not affected.
        Assert.True((await Auth(scope).LoginAsync(Password, "10.0.0.2", null, default)).Succeeded);
    }

    [Fact]
    public async Task Session_ExpiresAfterAYearWithoutUse()
    {
        var token = await SetUpAsync();
        using var scope = Open();

        _clock.Advance(AuthService.SessionLifetime - TimeSpan.FromMinutes(1));
        Assert.True((await Auth(scope).ValidateAsync(token, default)).Valid);

        // That check renewed it, so it takes a whole new year of silence to expire.
        _clock.Advance(AuthService.SessionLifetime + TimeSpan.FromMinutes(1));
        Assert.False((await Auth(scope).ValidateAsync(token, default)).Valid);
    }

    [Fact]
    public async Task Session_SlidesOnlyOncePerDay()
    {
        var token = await SetUpAsync();
        using var scope = Open();
        var auth = Auth(scope);

        _clock.Advance(TimeSpan.FromHours(23));
        Assert.Null((await auth.ValidateAsync(token, default)).RenewedUntil);

        _clock.Advance(TimeSpan.FromHours(2));
        var renewed = await auth.ValidateAsync(token, default);
        Assert.True(renewed.Valid);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime + AuthService.SessionLifetime, renewed.RenewedUntil);
    }

    [Fact]
    public async Task Validate_RejectsUnknownTokens()
    {
        await SetUpAsync();
        using var scope = Open();

        Assert.False((await Auth(scope).ValidateAsync("made-up-token", default)).Valid);
    }

    [Fact]
    public async Task Logout_RevokesOnlyThatDevice_AndLogoutEverywhereRevokesAll()
    {
        await SetUpAsync();
        using var scope = Open();
        var auth = Auth(scope);
        var phone = (await auth.LoginAsync(Password, Client, "Phone", default)).Token!;
        var laptop = (await auth.LoginAsync(Password, Client, "Laptop", default)).Token!;

        await auth.LogoutAsync(phone, default);
        Assert.False((await auth.ValidateAsync(phone, default)).Valid);
        Assert.True((await auth.ValidateAsync(laptop, default)).Valid);

        await auth.LogoutEverywhereAsync(default);
        Assert.False((await auth.ValidateAsync(laptop, default)).Valid);
    }

    [Fact]
    public async Task ChangePassword_KeepsThisDevice_SignsOutTheOthers_AndSwitchesThePassword()
    {
        await SetUpAsync();
        using var scope = Open();
        var auth = Auth(scope);
        var phone = (await auth.LoginAsync(Password, Client, "Phone", default)).Token!;
        var laptop = (await auth.LoginAsync(Password, Client, "Laptop", default)).Token!;

        var error = await auth.ChangePasswordAsync(Password, "a brand new password", laptop, Client, default);

        Assert.Equal(AuthError.None, error);
        Assert.True((await auth.ValidateAsync(laptop, default)).Valid);
        Assert.False((await auth.ValidateAsync(phone, default)).Valid);
        Assert.Equal(AuthError.InvalidPassword, (await auth.LoginAsync(Password, Client, null, default)).Error);
        Assert.True((await auth.LoginAsync("a brand new password", Client, null, default)).Succeeded);
    }

    [Fact]
    public async Task ChangePassword_RejectsAWrongCurrentPassword_AndAShortNewOne()
    {
        var token = await SetUpAsync();
        using var scope = Open();
        var auth = Auth(scope);

        Assert.Equal(AuthError.InvalidPassword, await auth.ChangePasswordAsync("wrong password", "a brand new password", token, Client, default));
        Assert.Equal(AuthError.WeakPassword, await auth.ChangePasswordAsync(Password, "short", token, Client, default));
        Assert.True((await auth.LoginAsync(Password, Client, null, default)).Succeeded);
    }

    [Fact]
    public async Task Reset_ReopensSetupOnce_AndALeftoverFlagDoesNotReopenItAgain()
    {
        var oldToken = await SetUpAsync();

        // Flag set and the app restarted: sessions are dropped and setup is open again.
        using (var scope = Open(reset: "true"))
        {
            var auth = Auth(scope);
            await auth.ApplyPendingResetAsync(default);

            Assert.False((await auth.ValidateAsync(oldToken, default)).Valid);
            Assert.Equal(new AuthStatus(SetupRequired: true, Authenticated: false), await auth.GetStatusAsync(null, default));
            Assert.Equal(AuthError.SetupRequired, (await auth.LoginAsync(Password, Client, null, default)).Error);

            Assert.True((await auth.SetupAsync("my new password", null, default)).Succeeded);
        }

        // Flag still "true" after another restart: the reset was used, so nothing re-opens and sessions survive.
        using (var scope = Open(reset: "true"))
        {
            var auth = Auth(scope);
            var login = await auth.LoginAsync("my new password", Client, null, default);
            await auth.ApplyPendingResetAsync(default);

            Assert.True(login.Succeeded);
            Assert.True((await auth.ValidateAsync(login.Token!, default)).Valid);
            Assert.Equal(AuthError.SetupNotAllowed, (await auth.SetupAsync("attacker password", null, default)).Error);
        }

        // A new flag value arms another reset.
        using (var scope = Open(reset: "reset-2"))
        {
            Assert.True(await Auth(scope).IsSetupOpenAsync(default));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("FALSE")]
    public async Task Reset_IsOffForEmptyOrFalseValues(string? reset)
    {
        await SetUpAsync();
        using var scope = Open(reset);

        Assert.False(await Auth(scope).IsSetupOpenAsync(default));
    }

    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch (IOException) { }
        }
    }

    private async Task<string> SetUpAsync()
    {
        using var scope = Open();
        var outcome = await Auth(scope).SetupAsync(Password, "Setup device", default);
        Assert.True(outcome.Succeeded);
        return outcome.Token!;
    }

    private static AuthService Auth(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AuthService>();

    private AppDbContext NewDb() => _providers[0].CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    private IServiceScope Open(string? reset = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(config);
        services.AddApplication();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(_throttle);
        services.Configure<AuthOptions>(o => o.ResetPassword = reset);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.CreateScope();
    }
}

/// <summary>Shared by the throttle tests, which don't need a database.</summary>
internal sealed class TestClock(DateTime start) : TimeProvider
{
    private DateTimeOffset _now = new(start, TimeSpan.Zero);
    public void Advance(TimeSpan by) => _now += by;
    public override DateTimeOffset GetUtcNow() => _now;
}
