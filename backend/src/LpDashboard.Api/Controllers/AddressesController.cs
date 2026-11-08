using LpDashboard.Application.Addresses;
using LpDashboard.Application.Sync;
using LpDashboard.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using LpDashboard.Application.Abstractions;

namespace LpDashboard.Api.Controllers;

public sealed record AddAddressRequest(string Address, string? Label = null);

public sealed record UpdateAddressRequest(string? Label);

public sealed record WatchedAddressDto(string Address, string ChainFamily, string Label);

[ApiController]
[Route("api/addresses")]
public sealed class AddressesController(IWatchedAddressRepository addresses, IUnitOfWork unitOfWork, SyncCoordinator sync, TimeProvider time)
    : ControllerBase
{
    private const int MaxLabelLength = 100;

    /// <summary>The wallets of the logged-in account, oldest first. This is the list every device shows.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WatchedAddressDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<WatchedAddressDto>> List(CancellationToken ct) =>
        (await addresses.ListAsync(ct)).Select(ToDto).ToList();

    /// <summary>
    /// Starts tracking a wallet (idempotent). A label is stored with the wallet; for a wallet that already has a label the
    /// existing one wins, so importing a browser's old list never overwrites a name set elsewhere (use PATCH to rename).
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

        if (CleanLabel(request.Label) is not { } label)
        {
            return LabelTooLong();
        }

        if (await addresses.FindAsync(parsed.Address, ct) is { } existing)
        {
            if (label.Length > 0 && string.IsNullOrEmpty(existing.Label))
            {
                existing.Label = label;
                await unitOfWork.SaveChangesAsync(ct);
            }
            return Ok(ToDto(existing));
        }

        var entity = new WatchedAddress
        {
            Address = parsed.Address,
            Label = label.Length > 0 ? label : null,
            ChainFamily = parsed.Family,
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        addresses.Add(entity);
        await unitOfWork.SaveChangesAsync(ct);
        sync.RequestSync(new SyncRequest(parsed.Address));

        return Ok(ToDto(entity));
    }

    /// <summary>Renames a wallet (an empty label clears it).</summary>
    [HttpPatch("{address}")]
    [ProducesResponseType<WatchedAddressDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rename(string address, UpdateAddressRequest request, CancellationToken ct)
    {
        if (CleanLabel(request.Label) is not { } label) return LabelTooLong();
        if (await Find(address, ct) is not { } entity) return NotFound();

        entity.Label = label.Length > 0 ? label : null;
        await unitOfWork.SaveChangesAsync(ct);
        return Ok(ToDto(entity));
    }

    /// <summary>
    /// Stops following a wallet: it leaves the list and background sync stops. Positions and loans already stored are kept,
    /// so adding the wallet again brings them back.
    /// </summary>
    [HttpDelete("{address}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(string address, CancellationToken ct)
    {
        if (await Find(address, ct) is not { } entity) return NotFound();

        addresses.Remove(entity);
        await unitOfWork.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<WatchedAddress?> Find(string address, CancellationToken ct) =>
        AddressParser.TryParse(address) is { } parsed ? await addresses.FindAsync(parsed.Address, ct) : null;

    /// <summary>Trimmed label ("" when empty), or null when it is too long.</summary>
    private static string? CleanLabel(string? label)
    {
        var value = label?.Trim() ?? string.Empty;
        return value.Length > MaxLabelLength ? null : value;
    }

    private IActionResult LabelTooLong() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Label too long", detail: $"Use at most {MaxLabelLength} characters.");

    private static WatchedAddressDto ToDto(WatchedAddress a) => new(a.Address, a.ChainFamily.ToString(), a.Label ?? string.Empty);
}
