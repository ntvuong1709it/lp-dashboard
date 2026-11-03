using LpDashboard.Application.Abstractions;
using LpDashboard.Application.Addresses;
using LpDashboard.Application.Sync;
using LpDashboard.Domain;
using Microsoft.AspNetCore.Mvc;

namespace LpDashboard.Api.Controllers;

/// <summary>Scope of a manual sync. <paramref name="Owner"/> is required: one wallet address or several, comma-separated.</summary>
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
        // A full sync of everything is left to the background schedule; a visitor can only sync the wallets they name.
        if (body is null || !OwnerParam.TryParse(body.Owner, out var owners))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid owner", detail: "Pass the wallet address(es) to sync.");
        }

        foreach (var owner in owners)
        {
            if (await addresses.FindAsync(owner, ct) is null)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown wallet", detail: "The wallet is not being watched.");
            }
        }

        if (body.TokenId is not null && (owners.Count != 1 || !body.TokenId.All(char.IsAsciiDigit) || body.TokenId.Length == 0))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid position", detail: "A position resync needs a wallet and a numeric token id.");
        }

        if (body.FromBlock is < 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid block", detail: "FromBlock must be 0 or greater.");
        }

        foreach (var owner in owners)
        {
            coordinator.RequestSync(new SyncRequest(owner, body.Chain, body.Protocol, body.TokenId, body.FromBlock));
        }
        return Accepted(Visible(coordinator.Status, owners));
    }

    /// <summary>Progress of the named wallets only; without a valid owner just the overall state is returned.</summary>
    [HttpGet("status")]
    public SyncStatus Status([FromQuery] string? owner)
    {
        OwnerParam.TryParse(owner, out var owners);
        return Visible(coordinator.Status, owners);
    }

    /// <summary>Tasks and errors name wallets and positions, so a visitor only gets those of their own wallets.</summary>
    private static SyncStatus Visible(SyncStatus status, IReadOnlyCollection<string> owners)
    {
        var tasks = status.Tasks.Where(t => owners.Contains(t.Owner)).ToList();
        return status with { Tasks = tasks, LastErrors = tasks.SelectMany(t => t.Errors).ToList(), RecentErrors = [] };
    }
}
