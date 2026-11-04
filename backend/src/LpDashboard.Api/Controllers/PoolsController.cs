using LpDashboard.Application.Pools;
using LpDashboard.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api/pools")]
public sealed class PoolsController(PoolExplorerService pools) : ControllerBase
{
    /// <summary>Pools from the analytics source. <c>owner</c> is optional; when given, pools where that wallet has an open position are marked.</summary>
    [HttpGet]
    [ProducesResponseType<PoolListDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> List(
        [FromQuery] Chain? chain, [FromQuery] Protocol? protocol, [FromQuery] decimal? minTvl,
        [FromQuery] string? sort, [FromQuery] int? limit, [FromQuery] string? owner, CancellationToken ct)
    {
        IReadOnlyCollection<string>? owners = null;
        if (!string.IsNullOrWhiteSpace(owner))
        {
            if (!OwnerParam.TryParse(owner, out var parsed))
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid owner");
            owners = parsed;
        }

        try
        {
            return Ok(await pools.ListAsync(new PoolQuery(chain ?? Chain.Base, protocol, minTvl ?? 0, sort, limit ?? 200, owners), ct));
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Pool data unavailable",
                detail: "The pool analytics source did not answer. Try again in a minute.");
        }
    }

    [HttpPost("{id}/estimate")]
    [ProducesResponseType<PoolEstimateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Estimate(string id, [FromBody] EstimateRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await pools.EstimateAsync(id, request, ct));
        }
        catch (EstimateException ex) when (ex.Failure == EstimateFailure.PoolNotFound)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Pool not found", detail: ex.Message);
        }
        catch (EstimateException ex)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Cannot estimate", detail: ex.Message);
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Price data unavailable",
                detail: "The price source did not answer. Try again in a minute.");
        }
    }
}
