using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LpDashboard.Infrastructure.Persistence;

/// <summary>
/// Sets the SQLite journal mode on every new connection. WAL (the default here) needs shared memory and breaks on
/// network/Windows bind mounts, so those setups should use DELETE or TRUNCATE.
/// </summary>
internal sealed class SqliteJournalModeInterceptor(string mode) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA journal_mode = {mode};";
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA journal_mode = {mode};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
