using LpDashboard.Application.Sync;
using Microsoft.Extensions.Options;

namespace LpDashboard.Api.Background;

/// <summary>Runs a sync on startup, then every Sync:IntervalMinutes or when triggered via POST /api/sync.</summary>
public sealed class SyncBackgroundService(SyncCoordinator coordinator, IOptions<SyncOptions> options, ILogger<SyncBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes));
        var runNow = options.Value.RunOnStartup;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (runNow)
            {
                try
                {
                    await coordinator.RunAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Sync run failed");
                }
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            wait.CancelAfter(interval);
            try
            {
                await coordinator.Triggers.WaitToReadAsync(wait.Token);
                coordinator.Triggers.TryRead(out _);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Interval elapsed.
            }
            runNow = true;
        }
    }
}
