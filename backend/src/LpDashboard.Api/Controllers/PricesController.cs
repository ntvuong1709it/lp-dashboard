using LpDashboard.Application.Pricing;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

[ApiController]
[Route("api/prices")]
public sealed class PricesController(MarketTickerService ticker) : ControllerBase
{
    /// <summary>Spot price and 24h change of BTC, ETH, BNB and AERO.</summary>
    [HttpGet("ticker")]
    [ProducesResponseType<TickerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Ticker(CancellationToken ct)
    {
        try
        {
            return Ok(await ticker.GetAsync(ct));
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Price data unavailable",
                detail: "The price source did not answer. Try again in a minute.");
        }
    }
}
