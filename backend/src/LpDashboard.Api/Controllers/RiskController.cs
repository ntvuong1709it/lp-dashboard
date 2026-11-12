using LpDashboard.Application.Pools;
using LpDashboard.Application.Risk;
using LpDashboard.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class RiskController(RiskService risk) : ControllerBase
{
    /// <summary>
    /// The risk verdict of a token with the evidence behind it. A check younger than the cache window is served as is; a
    /// missing or old one runs now. <c>refresh=true</c> forces a new check, refused with 429 while the cooldown after the
    /// last check runs. Any ERC-20 on Base can be looked up.
    /// </summary>
    [HttpGet("risk/tokens/{chain}/{address}")]
    [ProducesResponseType<TokenRiskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Token(Chain chain, string address, [FromQuery] bool refresh, CancellationToken ct)
    {
        try
        {
            return Ok(await risk.GetTokenAsync(chain, address, refresh, ct));
        }
        catch (RiskInputException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cannot check this address", detail: ex.Message);
        }
        catch (RiskCooldownException ex)
        {
            return Cooldown(ex);
        }
    }

    /// <summary>A pool's verdict: its own findings, the worst of its two tokens, and both token reports. <c>poolId</c> is the id the pool list uses.</summary>
    [HttpGet("risk/pools/{poolId}")]
    [ProducesResponseType<PoolRiskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Pool(string poolId, [FromQuery] bool refresh, CancellationToken ct)
    {
        try
        {
            return Ok(await risk.GetPoolAsync(poolId, refresh, ct));
        }
        catch (EstimateException ex) when (ex.Failure == EstimateFailure.PoolNotFound)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Pool not found", detail: ex.Message);
        }
        catch (EstimateException ex)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Cannot check this pool", detail: ex.Message);
        }
        catch (RiskInputException ex)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Cannot check this pool", detail: ex.Message);
        }
        catch (RiskCooldownException ex)
        {
            return Cooldown(ex);
        }
    }

    /// <summary>Tokens of the wallets' open positions and of starred pools, with their cached verdicts. Never calls a source.</summary>
    [HttpGet("risk/held")]
    [ProducesResponseType<IReadOnlyList<RiskHeldRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Held([FromQuery] string? owner, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        return Ok(await risk.GetHeldAsync(owners, ct));
    }

    /// <summary>Recent changes across held and starred tokens and pools: a finding appeared or cleared, a level moved.</summary>
    [HttpGet("risk/recent")]
    [ProducesResponseType<IReadOnlyList<RiskChangeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Recent([FromQuery] string? owner, [FromQuery] int? limit, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners)) return InvalidOwner();
        return Ok(await risk.GetRecentAsync(owners, limit ?? 30, ct));
    }

    /// <summary>Follows a pool: its tokens join the nightly re-check.</summary>
    [HttpPut("pools/{poolId}/star")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Star(string poolId, CancellationToken ct)
    {
        try
        {
            await risk.StarAsync(poolId, ct);
            return NoContent();
        }
        catch (EstimateException ex) when (ex.Failure == EstimateFailure.PoolNotFound)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Pool not found", detail: ex.Message);
        }
    }

    [HttpDelete("pools/{poolId}/star")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unstar(string poolId, CancellationToken ct)
    {
        await risk.UnstarAsync(poolId, ct);
        return NoContent();
    }

    private ObjectResult Cooldown(RiskCooldownException ex)
    {
        var seconds = (int)Math.Ceiling(ex.RetryAfter.TotalSeconds);
        Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Checked a moment ago",
            detail: $"This was checked recently. Try again in {seconds} s.");
    }

    private ObjectResult InvalidOwner() => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid owner");
}
