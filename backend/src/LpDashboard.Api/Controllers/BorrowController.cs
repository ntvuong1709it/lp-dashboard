using LpDashboard.Application.Borrow;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api/borrow")]
public sealed class BorrowController(BorrowQueryService queries, ManualLoanService manualLoans) : ControllerBase
{
    [HttpGet("assets")]
    public IReadOnlyList<AssetOptionDto> Assets() => AssetCatalog.Assets;

    [HttpGet("summary")]
    [ProducesResponseType<BorrowSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Summary([FromQuery] string? owner, [FromQuery] int? days, [FromQuery] string? timeZone, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        if (!TryZone(timeZone, out var zone)) return InvalidZone();
        return Ok(await queries.GetSummaryAsync(owners, days ?? 30, zone, ct));
    }

    [HttpGet("daily")]
    [ProducesResponseType<IReadOnlyList<DailyCoverageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Daily([FromQuery] string? owner, [FromQuery] int? days, [FromQuery] string? timeZone, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        if (!TryZone(timeZone, out var zone)) return InvalidZone();
        return Ok(await queries.GetDailyAsync(owners, days ?? 30, zone, ct));
    }

    [HttpGet("loans")]
    [ProducesResponseType<IReadOnlyList<LoanDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Loans([FromQuery] string? owner, [FromQuery] int? days, [FromQuery] string? timeZone, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        if (!TryZone(timeZone, out var zone)) return InvalidZone();
        return Ok(await queries.GetLoansAsync(owners, days ?? 30, zone, ct));
    }

    [HttpGet("collateral-history")]
    [ProducesResponseType<CollateralHistoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CollateralHistory(
        [FromQuery] string? owner, [FromQuery] int? days, [FromQuery] string? timeZone, [FromQuery] string? loan, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        if (!TryZone(timeZone, out var zone)) return InvalidZone();
        return Ok(await queries.GetCollateralHistoryAsync(owners, days ?? 30, zone, loan, ct));
    }

    [HttpGet("changes")]
    [ProducesResponseType<ChangesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Changes(
        [FromQuery] string? owner, [FromQuery] int? days, [FromQuery] string? timeZone, [FromQuery] string? loan, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        if (!TryZone(timeZone, out var zone)) return InvalidZone();
        return Ok(await queries.GetChangesAsync(owners, days ?? 30, zone, loan, ct));
    }

    [HttpGet("loans/manual")]
    [ProducesResponseType<IReadOnlyList<ManualLoanDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListManual([FromQuery] string? owner, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        return Ok(await manualLoans.ListAsync(owners, ct));
    }

    [HttpPost("loans/manual")]
    [ProducesResponseType<ManualLoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateManual(ManualLoanDto body, CancellationToken ct)
    {
        try
        {
            return Ok(await manualLoans.CreateAsync(body, ct));
        }
        catch (BorrowValidationException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid loan", detail: ex.Message);
        }
    }

    [HttpPut("loans/manual/{id:int}")]
    [ProducesResponseType<ManualLoanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateManual(int id, [FromQuery] string? owner, ManualLoanDto body, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners) || owners.Count != 1) return InvalidOwner();
        try
        {
            return await manualLoans.UpdateAsync(id, owners.First(), body, ct) is { } dto ? Ok(dto) : NotFound();
        }
        catch (BorrowValidationException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid loan", detail: ex.Message);
        }
    }

    [HttpDelete("loans/manual/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteManual(int id, [FromQuery] string? owner, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners) || owners.Count != 1) return InvalidOwner();
        return await manualLoans.DeleteAsync(id, owners.First(), ct) ? NoContent() : NotFound();
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
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid owner", detail: "Pass owner=<wallet address>[,<wallet address>...] (at most 20; one for edits).");

    private ObjectResult InvalidZone() => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown time zone");
}
