using LpDashboard.Application.Abstractions;

namespace LpDashboard.Application.Sync;

public enum SyncStage
{
    FetchingLatestBlock,
    DiscoveringPositions,
    FetchingEvents,
    SyncingPositions,
    Saving
}

/// <summary>
/// Receives step updates from <see cref="SyncService"/> and sub-step updates from providers.
/// Called inline on the sync thread.
/// </summary>
public interface ISyncProgress : IProgress<ProviderProgress>
{
    void Report(SyncStage stage, int positionsDone = 0, int positionsTotal = 0);
    void ReportError(string error);
}
