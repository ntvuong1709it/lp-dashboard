using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Sync;
using LpDashboard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

public sealed record HealthDto(
    string Status,
    bool DatabaseOk,
    IReadOnlyList<string> Providers,
    bool IsSyncing,
    DateTime? LastSyncFinishedAt,
    int LastSyncErrorCount);

[ApiController]
public sealed class HealthController(AppDbContext db, IEnumerable<IPositionProvider> providers, SyncCoordinator coordinator) : ControllerBase
{
    /// <summary>200 when the database is reachable; "Degraded" when no chain provider (RPC URL) is configured.</summary>
    [HttpGet("/health")]
    [HttpGet("/api/health")]
    [ProducesResponseType<HealthDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthDto>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var databaseOk = await db.Database.CanConnectAsync(ct);
        var configured = providers.Select(p => $"{p.Chain}/{p.Protocol}").ToList();
        var status = coordinator.Status;

        var dto = new HealthDto(
            !databaseOk ? "Unhealthy" : configured.Count == 0 ? "Degraded" : "Healthy",
            databaseOk,
            configured,
            status.IsRunning,
            status.LastFinishedAt,
            status.LastErrors.Count);
        return databaseOk ? Ok(dto) : StatusCode(StatusCodes.Status503ServiceUnavailable, dto);
    }
}
