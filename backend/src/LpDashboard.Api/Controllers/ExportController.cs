using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Addresses;
using LpDashboard.Application.Export;
using LpDashboard.Application.Queries;
using LpDashboard.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

/// <summary>CSV downloads. Filters match the dashboard: owner (default: every watched wallet) and, where it applies, status.</summary>
[ApiController]
[Route("api/export")]
public sealed class ExportController(ExportService export, PositionQueryService queries, TimeProvider time) : ControllerBase
{
    private const string ContentType = "text/csv; charset=utf-8";

    [HttpGet("positions.csv")]
    public async Task<IActionResult> Positions([FromQuery] string? owner, [FromQuery] PositionStatus? status, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        return Csv(await export.PositionsAsync(new PositionFilter(owners, status), ct), "positions");
    }

    [HttpGet("events.csv")]
    public async Task<IActionResult> Events([FromQuery] string? owner, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        return Csv(await export.EventsAsync(new PositionFilter(owners), ct), "events");
    }

    [HttpGet("fee-claims.csv")]
    public async Task<IActionResult> FeeClaims([FromQuery] string? owner, [FromQuery] string? timeZone, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        if (!TryZone(timeZone, out var zone)) return InvalidZone();
        return Csv(await export.FeeClaimsAsync(new ClaimFilter(owners), zone, ct), "fee-claims");
    }

    [HttpGet("daily-earnings.csv")]
    public async Task<IActionResult> DailyEarnings([FromQuery] string? owner, [FromQuery] string? timeZone, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        if (!TryZone(timeZone, out var zone)) return InvalidZone();
        return Csv(await export.DailyEarningsAsync(new PositionFilter(owners), zone, ct), "daily-earnings");
    }

    private FileContentResult Csv(byte[] bytes, string name) =>
        File(bytes, ContentType, $"lp-dashboard-{name}-{time.GetUtcNow():yyyyMMdd-HHmm}.csv");

    private static bool TryZone(string? timeZone, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        return string.IsNullOrWhiteSpace(timeZone) || TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out zone!);
    }

    private ObjectResult InvalidOwner() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid owner", detail: "Pass owner=<wallet address>[,<wallet address>...] (at most 20).");

    private ObjectResult InvalidZone() => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown time zone");
}
