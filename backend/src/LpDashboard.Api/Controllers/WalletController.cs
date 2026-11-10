using LpDashboard.Application.Pools;
using LpDashboard.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api/wallet")]
public sealed class WalletController(WalletBalanceService balances) : ControllerBase
{
    /// <summary>Token and native balances of one watched wallet. <c>tokens</c> is a comma-separated list of token addresses.</summary>
    [HttpGet("balances")]
    [ProducesResponseType<WalletBalancesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Balances([FromQuery] string? owner, [FromQuery] string? tokens, [FromQuery] Chain? chain, CancellationToken ct)
    {
        if (!OwnerParam.TryParse(owner, out var owners) || owners.Count != 1)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid owner", detail: "Pass exactly one wallet address.");

        var list = (tokens ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            return Ok(await balances.GetAsync(owners.First(), list, chain ?? Chain.Base, ct));
        }
        catch (WalletBalanceException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Cannot read balances", detail: ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Balances unavailable", detail: "The chain did not answer. Try again in a moment.");
        }
    }
}
