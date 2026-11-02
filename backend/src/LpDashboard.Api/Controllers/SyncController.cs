using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Addresses;
using LpDashboard.Application.Sync;
using LpDashboard.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

/// <summary>Optional scope for a manual sync; an empty body is a full sync.</summary>
public sealed record SyncRequestBody(string? Owner, Chain? Chain, Protocol? Protocol, string? TokenId, long? FromBlock);

[ApiController]
[Route("api/sync")]
public sealed class SyncController(SyncCoordinator coordinator, IWatchedAddressRepository addresses) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Trigger([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SyncRequestBody? body, CancellationToken ct)
    {
        if (body is null)
        {
            coordinator.RequestSync();
            return Accepted(coordinator.Status);
        }

        string? owner = null;
        if (!string.IsNullOrWhiteSpace(body.Owner))
        {
            owner = AddressParser.TryParse(body.Owner)?.Address;
            if (owner is null || await addresses.FindAsync(owner, ct) is null)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown wallet", detail: "The wallet is not being watched.");
            }
        }

        if (body.TokenId is not null && (owner is null || !body.TokenId.All(char.IsAsciiDigit) || body.TokenId.Length == 0))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid position", detail: "A position resync needs a wallet and a numeric token id.");
        }

        if (body.FromBlock is < 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid block", detail: "FromBlock must be 0 or greater.");
        }

        coordinator.RequestSync(new SyncRequest(owner, body.Chain, body.Protocol, body.TokenId, body.FromBlock));
        return Accepted(coordinator.Status);
    }

    [HttpGet("status")]
    public SyncStatus Status() => coordinator.Status;
}
