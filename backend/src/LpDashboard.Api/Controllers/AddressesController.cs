using LpDashboard.Application.Addresses;
using LpDashboard.Application.Sync;
using LpDashboard.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using LpDashboard.Application.Abstractions;

namespace LpDashboard.Api.Controllers;

public sealed record AddAddressRequest(string Address, string? Label);

public sealed record WatchedAddressDto(int Id, string Address, string? Label, string ChainFamily, DateTime CreatedAt);

[ApiController]
[Route("api/addresses")]
public sealed class AddressesController(IWatchedAddressRepository addresses, IUnitOfWork unitOfWork, SyncCoordinator sync, TimeProvider time)
    : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<WatchedAddressDto>> List(CancellationToken ct) =>
        (await addresses.ListAsync(ct)).Select(ToDto).ToList();

    [HttpPost]
    [ProducesResponseType<WatchedAddressDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add(AddAddressRequest request, CancellationToken ct)
    {
        if (AddressParser.TryParse(request.Address) is not { } parsed)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid address", detail: "Expected an EVM (0x...) or Solana address.");
        }

        var label = request.Label?.Trim();
        if (label is { Length: > 100 })
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Label too long", detail: "Max 100 characters.");
        }

        if (await addresses.FindAsync(parsed.Address, ct) is not null)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Address already watched");
        }

        var entity = new WatchedAddress
        {
            Address = parsed.Address,
            Label = string.IsNullOrEmpty(label) ? null : label,
            ChainFamily = parsed.Family,
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        addresses.Add(entity);
        await unitOfWork.SaveChangesAsync(ct);
        sync.RequestSync();

        return CreatedAtAction(nameof(List), null, ToDto(entity));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
    {
        var entity = await addresses.GetAsync(id, ct);
        if (entity is null) return NotFound();

        addresses.Remove(entity);
        await unitOfWork.SaveChangesAsync(ct);
        return NoContent();
    }

    private static WatchedAddressDto ToDto(WatchedAddress a) => new(a.Id, a.Address, a.Label, a.ChainFamily.ToString(), a.CreatedAt);
}
