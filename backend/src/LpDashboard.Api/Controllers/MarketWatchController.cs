using LpDashboard.Application.Market;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api/market")]
public sealed class MarketWatchController(MarketWatchService market) : ControllerBase
{
    /// <summary>
    /// What is coming that can move stocks and crypto (macro events and big-company earnings), what just happened with a
    /// risk-on / risk-off tag, the latest headlines, and a stress table for the loan with the worst risk. The calendar does
    /// not depend on the wallet; <c>owner</c> is optional and only adds the loan. <c>days</c> is 7 or 14 (anything else is 14).
    /// </summary>
    [HttpGet("watch")]
    [ProducesResponseType<MarketWatchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Watch([FromQuery] string? owner, [FromQuery] int? days, [FromQuery] string? timeZone, CancellationToken ct)
    {
        IReadOnlyCollection<string>? owners = null;
        if (!string.IsNullOrWhiteSpace(owner))
        {
            if (!OwnerParam.TryParse(owner, out var parsed)) return InvalidOwner();
            owners = parsed;
        }
        if (!TryZone(timeZone, out var zone)) return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown time zone");

        return Ok(await market.GetAsync(days ?? 14, owners, zone, ct));
    }

    private static bool TryZone(string? timeZone, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timeZone)) return true;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found)) return false;
        zone = found;
        return true;
    }

    private ObjectResult InvalidOwner() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid owner", detail: "Pass owner=<wallet address>[,<wallet address>...] (at most 20).");
}
