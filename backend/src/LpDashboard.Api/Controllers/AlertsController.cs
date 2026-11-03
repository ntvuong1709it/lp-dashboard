using LpDashboard.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

public sealed record AlertStatusDto(bool Configured);

public sealed record AlertTestResultDto(bool Sent, string? Error);

[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(IAlertNotifier notifier) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<AlertStatusDto>(StatusCodes.Status200OK)]
    public IActionResult Status() => Ok(new AlertStatusDto(notifier.IsConfigured));

    /// <summary>Sends a test message so the channel setup can be checked without waiting for a real alert.</summary>
    [HttpPost("test")]
    [ProducesResponseType<AlertTestResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var error = await notifier.SendAsync("LP Dashboard: test alert. Out-of-range alerts are set up correctly.", ct);
        return Ok(new AlertTestResultDto(error is null, error));
    }
}
