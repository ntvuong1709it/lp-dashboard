using LpDashboard.Application.Risk;
using LpDashboard.Application.Sync;
using Microsoft.Extensions.Options;

namespace LpDashboard.Api.Background;

/// <summary>
/// Runs the risk screen's scheduled work: pool balance snapshots every <c>Risk:LiquidityIntervalMinutes</c> and the nightly
/// re-check at <c>Risk:NightlyUtcHour</c>. A catch-up run shortly after startup re-checks only what went stale while the app
/// was off. Every run waits for a position sync to finish first, so a slow outside API can never delay a sync.
/// </summary>
public sealed class RiskBackgroundService(
    IServiceScopeFactory scopeFactory,
    SyncCoordinator sync,
    IOptions<RiskSettings> settings,
    TimeProvider time,
    ILogger<RiskBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SyncPoll = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxSyncWait = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await RunAsync("catch-up", (m, ct) => m.RunNightlyAsync(onlyStale: true, ct), stoppingToken);

            var liquidityEvery = TimeSpan.FromMinutes(Math.Max(5, settings.Value.LiquidityIntervalMinutes));
            var nextLiquidity = time.GetUtcNow();
            var nextNightly = NextNightly(time.GetUtcNow());

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = time.GetUtcNow();
                if (now >= nextLiquidity)
                {
                    await RunAsync("liquidity", (m, ct) => m.RunLiquidityAsync(ct), stoppingToken);
                    nextLiquidity = time.GetUtcNow() + liquidityEvery;
                }
                if (now >= nextNightly)
                {
                    await RunAsync("nightly", (m, ct) => m.RunNightlyAsync(onlyStale: false, ct), stoppingToken);
                    nextNightly = NextNightly(time.GetUtcNow());
                }

                var due = (nextLiquidity < nextNightly ? nextLiquidity : nextNightly) - time.GetUtcNow();
                await Task.Delay(due < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : due, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private DateTimeOffset NextNightly(DateTimeOffset now)
    {
        var hour = Math.Clamp(settings.Value.NightlyUtcHour, 0, 23);
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero);
        return today > now ? today : today.AddDays(1);
    }

    private async Task RunAsync(string what, Func<RiskMonitorService, CancellationToken, Task> run, CancellationToken ct)
    {
        try
        {
            var waited = TimeSpan.Zero;
            while (sync.Status.IsRunning && waited < MaxSyncWait)
            {
                await Task.Delay(SyncPoll, ct);
                waited += SyncPoll;
            }

            // Its own scope per run: the repositories are scoped and a run can be long.
            using var scope = scopeFactory.CreateScope();
            await run(scope.ServiceProvider.GetRequiredService<RiskMonitorService>(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Risk {What} run failed", what);
        }
    }
}
