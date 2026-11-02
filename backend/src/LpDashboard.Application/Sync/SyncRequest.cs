using LpDashboard.Domain;

namespace LpDashboard.Application.Sync;

/// <summary>
/// What a sync run should cover. All-null is a full sync of every watched wallet.
/// <paramref name="TokenId"/> limits the run to one position (needs <paramref name="Owner"/>);
/// <paramref name="FromBlock"/> re-fetches events from that block (already stored events are skipped, so this only fills gaps).
/// </summary>
public sealed record SyncRequest(
    string? Owner = null,
    Chain? Chain = null,
    Protocol? Protocol = null,
    string? TokenId = null,
    long? FromBlock = null)
{
    public static SyncRequest Full { get; } = new();

    public bool IsFull => this == Full;

    public string? Describe()
    {
        if (IsFull) return null;
        var parts = new List<string>();
        if (TokenId is not null) parts.Add($"position #{TokenId}");
        else if (Owner is not null) parts.Add("one wallet");
        if (FromBlock is { } block) parts.Add($"rescan from block {block}");
        return string.Join(", ", parts);
    }
}
