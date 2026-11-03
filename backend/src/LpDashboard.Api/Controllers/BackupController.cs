using LpDashboard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api/backup")]
public sealed class BackupController(AppDbContext db, TimeProvider time, IConfiguration configuration) : ControllerBase
{
    /// <summary>Consistent copy of the SQLite database (price cache and snapshots cannot be recreated from chain data).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<IActionResult> Download(CancellationToken ct)
    {
        // The file holds every wallet anyone ever entered, so it is off unless the operator turns it on (Backup:Enabled).
        if (!configuration.GetValue<bool>("Backup:Enabled")) return NotFound();

        if (!db.Database.IsSqlite())
        {
            return Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Backup is only available for SQLite", detail: "Use pg_dump for Postgres.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"lpdash-backup-{Guid.NewGuid():N}.db");
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [path], ct);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        return File(stream, "application/octet-stream", $"lpdashboard-{time.GetUtcNow():yyyyMMdd-HHmmss}.db");
    }
}
