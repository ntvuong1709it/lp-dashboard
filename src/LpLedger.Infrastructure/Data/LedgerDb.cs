using System.Numerics;
using LpLedger.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LpLedger.Infrastructure.Data;

// Token amounts and IDs are uint256, which no database integer holds, so they are stored as decimal text.

public sealed class CursorRow { public string Wallet { get; set; } = ""; public string Key { get; set; } = ""; public long Block { get; set; } }

public sealed class NftTransferRow
{
    public string Wallet { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Contract { get; set; } = "";
    public BigInteger TokenId { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public long Block { get; set; }
    public long Timestamp { get; set; }
}

public sealed class WalletTxRow { public string Wallet { get; set; } = ""; public string Hash { get; set; } = ""; }

/// <summary>Marks a transaction as decoded, so one with no position-manager events is not fetched again.</summary>
public sealed class ReceiptRow { public string Hash { get; set; } = ""; }

public sealed class EventRow
{
    public string Hash { get; set; } = "";
    public int LogIndex { get; set; }
    public string Manager { get; set; } = "";
    public BigInteger TokenId { get; set; }
    public EventKind Kind { get; set; }
    public BigInteger Amount0 { get; set; }
    public BigInteger Amount1 { get; set; }
    public long Block { get; set; }
}

public sealed class BlockTimeRow { public long Block { get; set; } public long Timestamp { get; set; } }

public sealed class LedgerDb(DbContextOptions<LedgerDb> options) : DbContext(options)
{
    public DbSet<CursorRow> Cursors => Set<CursorRow>();
    public DbSet<NftTransferRow> NftTransfers => Set<NftTransferRow>();
    public DbSet<WalletTxRow> WalletTxs => Set<WalletTxRow>();
    public DbSet<ReceiptRow> Receipts => Set<ReceiptRow>();
    public DbSet<EventRow> Events => Set<EventRow>();
    public DbSet<BlockTimeRow> BlockTimes => Set<BlockTimeRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<BigInteger>().HaveConversion<BigIntegerToText>().HaveMaxLength(80);
        b.Properties<EventKind>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<string>().HaveMaxLength(100);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<CursorRow>().ToTable("scan_cursors").HasKey(e => new { e.Wallet, e.Key });
        m.Entity<NftTransferRow>().ToTable("nft_transfers").HasKey(e => new { e.Wallet, e.Hash, e.Contract, e.TokenId, e.From, e.To });
        m.Entity<NftTransferRow>().HasIndex(e => e.Wallet);
        m.Entity<WalletTxRow>().ToTable("wallet_txs").HasKey(e => new { e.Wallet, e.Hash });
        m.Entity<ReceiptRow>().ToTable("receipts").HasKey(e => e.Hash);
        m.Entity<EventRow>().ToTable("position_events").HasKey(e => new { e.Hash, e.LogIndex });
        m.Entity<BlockTimeRow>().ToTable("block_times").HasKey(e => e.Block);
        m.Entity<BlockTimeRow>().Property(e => e.Block).ValueGeneratedNever();
    }

    private sealed class BigIntegerToText() : ValueConverter<BigInteger, string>(v => v.ToString(), s => BigInteger.Parse(s));
}
