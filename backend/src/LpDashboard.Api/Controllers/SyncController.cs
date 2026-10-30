using LpDashboard.Application.Sync;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api/sync")]
public sealed class SyncController(SyncCoordinator coordinator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult Trigger()
    {
        coordinator.RequestSync();
        return Accepted(coordinator.Status);
    }

    [HttpGet("status")]
    public SyncStatus Status() => coordinator.Status;
}
