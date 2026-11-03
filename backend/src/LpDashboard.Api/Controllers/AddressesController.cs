using LpDashboard.Application.Addresses;
using LpDashboard.Application.Sync;
using LpDashboard.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using LpDashboard.Application.Abstractions;

namespace LpDashboard.Api.Controllers;

public sealed record AddAddressRequest(string Address);

public sealed record WatchedAddressDto(string Address, string ChainFamily);

[ApiController]
[Route("api/addresses")]
public sealed class AddressesController(IWatchedAddressRepository addresses, IUnitOfWork unitOfWork, SyncCoordinator sync, TimeProvider time)
    : ControllerBase
{
    /// <summary>
    /// Starts tracking a wallet (idempotent). The server keeps only the address and its chain data; labels and the
    /// list of wallets a visitor follows are stored in their browser, and the API never lists watched wallets.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<WatchedAddressDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Add(AddAddressRequest request, CancellationToken ct)
    {
        if (AddressParser.TryParse(request.Address) is not { } parsed)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid address", detail: "Expected an EVM (0x...) or Solana address.");
        }

        if (await addresses.FindAsync(parsed.Address, ct) is not null)
        {
            return Ok(new WatchedAddressDto(parsed.Address, parsed.Family.ToString()));
        }

        var entity = new WatchedAddress
        {
            Address = parsed.Address,
            ChainFamily = parsed.Family,
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        addresses.Add(entity);
        await unitOfWork.SaveChangesAsync(ct);
        sync.RequestSync(new SyncRequest(parsed.Address));

        return Ok(ToDto(entity));
    }

    private static WatchedAddressDto ToDto(WatchedAddress a) => new(a.Address, a.ChainFamily.ToString());
}
