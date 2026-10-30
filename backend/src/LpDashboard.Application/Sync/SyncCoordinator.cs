using System.Threading.Channels;
using LpDashboard.Application.Abstractions;
using LpDashboard.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LpDashboard.Application.Sync;

public enum SyncTaskState
{
    Pending,
    Running,
    Succeeded,
    CompletedWithErrors,
    Failed
}

/// <summary>Progress of one (owner, chain, protocol) unit of work within a sync run.</summary>
public sealed record SyncTaskStatus
{
    public Chain Chain { get; init; }
    public Protocol Protocol { get; init; }
    public string Owner { get; init; } = string.Empty;
    public SyncTaskState State { get; init; }
    public SyncStage? Stage { get; init; }
    /// <summary>Sub-step of the current stage, e.g. "Fetching transaction receipts".</summary>
    public string? Detail { get; init; }
    public int DetailDone { get; init; }
    /// <summary>0 when the sub-step size is unknown.</summary>
    public int DetailTotal { get; init; }
    public int PositionsDone { get; init; }
    public int PositionsTotal { get; init; }
    public double Percent { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record SyncStatus
{
    public bool IsRunning { get; init; }
    /// <summary>A manual sync was requested and will start after the current run (if any).</summary>
    public bool IsQueued { get; init; }
    /// <summary>Overall progress of the current run (0-100); 100 once it finished.</summary>
    public double Percent { get; init; }
    public DateTime? LastStartedAt { get; init; }
    public DateTime? LastFinishedAt { get; init; }
    public IReadOnlyList<string> ConfiguredProviders { get; init; } = [];
    /// <summary>Tasks of the current run, or of the last finished run when idle.</summary>
    public IReadOnlyList<SyncTaskStatus> Tasks { get; init; } = [];
    /// <summary>Errors of the current run so far, or of the last finished run when idle.</summary>
    public IReadOnlyList<string> LastErrors { get; init; } = [];
}

/// <summary>Singleton that serializes sync runs and accepts manual triggers.</summary>
public sealed class SyncCoordinator(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<SyncCoordinator> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _statusLock = new();
    private readonly Channel<bool> _trigger = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private volatile SyncStatus _status = new();

    public SyncStatus Status => _status;

    public ChannelReader<bool> Triggers => _trigger.Reader;

    public void RequestSync()
    {
        _trigger.Writer.TryWrite(true);
        Update(s => s with { IsQueued = true });
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return;

        try
        {
            Update(s => s with
            {
                IsRunning = true,
                IsQueued = HasPendingTrigger(),
                Percent = 0,
                LastStartedAt = time.GetUtcNow().UtcDateTime,
                Tasks = [],
                LastErrors = []
            });

            List<(Chain Chain, Protocol Protocol)> targets;
            List<string> owners;
            using (var scope = scopeFactory.CreateScope())
            {
                targets = scope.ServiceProvider.GetServices<IPositionProvider>().Select(p => (p.Chain, p.Protocol)).ToList();
                owners = (await scope.ServiceProvider.GetRequiredService<IWatchedAddressRepository>().ListAsync(ct))
                    .Where(a => a.ChainFamily == ChainFamily.Evm)
                    .Select(a => a.Address)
                    .ToList();
            }

            var tasks = owners
                .SelectMany(owner => targets.Select(t => new SyncTaskStatus { Chain = t.Chain, Protocol = t.Protocol, Owner = owner }))
                .ToList();
            Update(s => s with
            {
                ConfiguredProviders = targets.Select(t => $"{t.Chain}/{t.Protocol}").ToList(),
                Tasks = tasks
            });
            if (targets.Count == 0)
            {
                AddError("No chain providers configured. Set Evm:Chains:<n>:RpcUrl (e.g. via user-secrets).");
            }

            for (var i = 0; i < tasks.Count; i++)
            {
                var (chain, protocol, owner) = (tasks[i].Chain, tasks[i].Protocol, tasks[i].Owner);
                var progress = new TaskProgress(this, i);
                UpdateTask(i, t => t with { State = SyncTaskState.Running, StartedAt = time.GetUtcNow().UtcDateTime });

                using var scope = scopeFactory.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<SyncService>();
                try
                {
                    var errors = await sync.SyncAsync(chain, protocol, owner, ct, progress);
                    UpdateTask(i, t => t with
                    {
                        State = errors.Count == 0 ? SyncTaskState.Succeeded : SyncTaskState.CompletedWithErrors,
                        Stage = null,
                        Detail = null,
                        Percent = 100,
                        FinishedAt = time.GetUtcNow().UtcDateTime
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Sync failed for {Chain}/{Protocol} {Owner}", chain, protocol, owner);
                    progress.ReportError($"{chain}/{protocol} {owner}: {ex.Message}");
                    UpdateTask(i, t => t with
                    {
                        State = SyncTaskState.Failed,
                        Percent = 100,
                        FinishedAt = time.GetUtcNow().UtcDateTime
                    });
                }
            }
        }
        finally
        {
            Update(s => s with
            {
                IsRunning = false,
                IsQueued = HasPendingTrigger(),
                Percent = 100,
                LastFinishedAt = time.GetUtcNow().UtcDateTime
            });
            _gate.Release();
        }
    }

    private bool HasPendingTrigger() => _trigger.Reader.CanCount && _trigger.Reader.Count > 0;

    private void Update(Func<SyncStatus, SyncStatus> change)
    {
        lock (_statusLock)
        {
            _status = change(_status);
        }
    }

    private void UpdateTask(int index, Func<SyncTaskStatus, SyncTaskStatus> change) => Update(s =>
    {
        var tasks = s.Tasks.ToArray();
        tasks[index] = change(tasks[index]);
        return s with { Tasks = tasks, Percent = Math.Round(tasks.Average(t => t.Percent), 1) };
    });

    private void AddError(string error) => Update(s => s with { LastErrors = [.. s.LastErrors, error] });

    private sealed class TaskProgress(SyncCoordinator coordinator, int index) : ISyncProgress
    {
        public void Report(SyncStage stage, int positionsDone = 0, int positionsTotal = 0) =>
            coordinator.UpdateTask(index, t => t with
            {
                Stage = stage,
                Detail = null,
                DetailDone = 0,
                DetailTotal = 0,
                PositionsDone = positionsDone,
                PositionsTotal = positionsTotal,
                Percent = Math.Max(t.Percent, Math.Round(StageStart(stage, positionsDone, positionsTotal), 1))
            });

        public void Report(ProviderProgress value) =>
            coordinator.UpdateTask(index, t =>
            {
                var percent = t.Percent;
                if (t.Stage is { } stage && value.Total > 0)
                {
                    var start = StageStart(stage, t.PositionsDone, t.PositionsTotal);
                    var end = StageEnd(stage, t.PositionsDone, t.PositionsTotal);
                    percent = Math.Max(percent, Math.Round(start + (end - start) * Math.Min(1, (double)value.Done / value.Total), 1));
                }
                return t with { Detail = value.Step, DetailDone = value.Done, DetailTotal = value.Total, Percent = percent };
            });

        public void ReportError(string error) => coordinator.Update(s =>
        {
            var tasks = s.Tasks.ToArray();
            tasks[index] = tasks[index] with { Errors = [.. tasks[index].Errors, error] };
            return s with { Tasks = tasks, LastErrors = [.. s.LastErrors, error] };
        });

        // Rough weights: event fetching (receipts at a paced RPC rate) dominates first syncs, then each position costs about the same.
        private static double StageStart(SyncStage stage, int done, int total) => stage switch
        {
            SyncStage.FetchingLatestBlock => 0,
            SyncStage.DiscoveringPositions => 5,
            SyncStage.FetchingEvents => 15,
            SyncStage.SyncingPositions => 60 + (total == 0 ? 35 : 35.0 * done / total),
            SyncStage.Saving => 95,
            _ => 0
        };

        private static double StageEnd(SyncStage stage, int done, int total) => stage switch
        {
            SyncStage.SyncingPositions => StageStart(stage, Math.Min(done + 1, total), total),
            SyncStage.Saving => 100,
            _ => StageStart(stage + 1, 0, total)
        };
    }
}
