using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Addresses;
using LpDashboard.Application.Queries;
using LpDashboard.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class PositionsController(PositionQueryService queries) : ControllerBase
{
    [HttpGet("positions")]
    [ProducesResponseType<IReadOnlyList<PositionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] string? owner, [FromQuery] PositionStatus? status, [FromQuery] Protocol? protocol, [FromQuery] Chain? chain, CancellationToken ct)
    {
        if (!TryOwner(owner, out var normalized)) return InvalidOwner();
        var owners = await queries.ResolveOwnersAsync(normalized, ct);
        return Ok(await queries.ListAsync(new PositionFilter(owners, status, protocol, chain), ct));
    }

    [HttpGet("positions/{id:int}")]
    [ProducesResponseType<PositionDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        await queries.GetAsync(id, ct) is { } detail ? Ok(detail) : NotFound();

    [HttpGet("positions/{id:int}/snapshots")]
    [ProducesResponseType<IReadOnlyList<SnapshotPointDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Snapshots(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        await queries.GetSnapshotsAsync(id, from?.ToUniversalTime(), to?.ToUniversalTime(), ct) is { } points ? Ok(points) : NotFound();

    [HttpGet("positions/{id:int}/earnings/daily")]
    [ProducesResponseType<IReadOnlyList<DailyEarningsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PositionDailyEarnings(int id, [FromQuery] string? timeZone, CancellationToken ct)
    {
        var zone = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(timeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out zone))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown time zone");
        }

        return await queries.GetPositionDailyEarningsAsync(id, zone!, ct) is { } days ? Ok(days) : NotFound();
    }

    [HttpGet("portfolio/summary")]
    [ProducesResponseType<PortfolioSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Summary(
        [FromQuery] string? owner, [FromQuery] PositionStatus? status, [FromQuery] Protocol? protocol, [FromQuery] Chain? chain, CancellationToken ct)
    {
        if (!TryOwner(owner, out var normalized)) return InvalidOwner();
        var owners = await queries.ResolveOwnersAsync(normalized, ct);
        return Ok(await queries.GetSummaryAsync(new PositionFilter(owners, status, protocol, chain), ct));
    }

    [HttpGet("fees/claims")]
    [ProducesResponseType<IReadOnlyList<WeeklyClaimsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> WeeklyClaims(
        [FromQuery] string? owner, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? timeZone, CancellationToken ct)
    {
        if (!TryOwner(owner, out var normalized)) return InvalidOwner();

        var zone = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(timeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out zone))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown time zone");
        }

        var owners = await queries.ResolveOwnersAsync(normalized, ct);
        return Ok(await queries.GetWeeklyClaimsAsync(new ClaimFilter(owners, from?.ToUniversalTime(), to?.ToUniversalTime()), zone!, ct));
    }

    [HttpGet("fees/daily")]
    [ProducesResponseType<IReadOnlyList<DailyEarningsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DailyEarnings([FromQuery] string? owner, [FromQuery] string? timeZone, CancellationToken ct)
    {
        if (!TryOwner(owner, out var normalized)) return InvalidOwner();

        var zone = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(timeZone) && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out zone))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown time zone");
        }

        var owners = await queries.ResolveOwnersAsync(normalized, ct);
        return Ok(await queries.GetDailyEarningsAsync(new PositionFilter(owners), zone!, ct));
    }

    private static bool TryOwner(string? owner, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(owner)) return true;
        if (AddressParser.TryParse(owner) is not { } parsed) return false;
        normalized = parsed.Address;
        return true;
    }

    private ObjectResult InvalidOwner() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid owner address");
}
