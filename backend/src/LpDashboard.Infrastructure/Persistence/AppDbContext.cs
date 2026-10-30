using System.Numerics;
using LpDashboard.Domain;
using LpDashboard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LpDashboard.Infrastructure.Persistence;

/// <summary>Provider-agnostic model: no provider-specific SQL; big integers as strings; DateTimes always UTC.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WatchedAddress> WatchedAddresses => Set<WatchedAddress>();
    public DbSet<Token> Tokens => Set<Token>();
    public DbSet<Pool> Pools => Set<Pool>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionEvent> PositionEvents => Set<PositionEvent>();
    public DbSet<PositionSnapshot> PositionSnapshots => Set<PositionSnapshot>();
    public DbSet<TokenPrice> TokenPrices => Set<TokenPrice>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Chain>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<Protocol>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<ChainFamily>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<PositionStatus>().HaveConversion<string>().HaveMaxLength(16);
        builder.Properties<PositionEventType>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<decimal>().HavePrecision(38, 18);
        builder.Properties<decimal?>().HavePrecision(38, 18);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WatchedAddress>(e =>
        {
            e.Property(x => x.Address).HasMaxLength(64);
            e.Property(x => x.Label).HasMaxLength(100);
            e.HasIndex(x => x.Address).IsUnique();
        });

        modelBuilder.Entity<Token>(e =>
        {
            e.Property(x => x.Address).HasMaxLength(64);
            e.Property(x => x.Symbol).HasMaxLength(64);
            e.HasIndex(x => new { x.Chain, x.Address }).IsUnique();
        });

        modelBuilder.Entity<Pool>(e =>
        {
            e.Property(x => x.Address).HasMaxLength(64);
            e.HasIndex(x => new { x.Chain, x.Address }).IsUnique();
            e.HasOne(x => x.Token0).WithMany().HasForeignKey(x => x.Token0Id).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Token1).WithMany().HasForeignKey(x => x.Token1Id).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Position>(e =>
        {
            e.Property(x => x.PositionManager).HasMaxLength(64);
            e.Property(x => x.TokenId).HasMaxLength(80);
            e.Property(x => x.Owner).HasMaxLength(64);
            e.Property(x => x.Gauge).HasMaxLength(64);
            e.Property(x => x.RewardTokenAddress).HasMaxLength(64);
            e.Property(x => x.RewardTokenSymbol).HasMaxLength(64);
            e.Property(x => x.Liquidity).HasMaxLength(80);
            e.Property(x => x.SqrtPriceX96).HasMaxLength(80);
            e.HasIndex(x => new { x.Chain, x.PositionManager, x.TokenId }).IsUnique();
            e.HasIndex(x => x.Owner);
            e.HasOne(x => x.Pool).WithMany().HasForeignKey(x => x.PoolId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Events).WithOne(x => x.Position).HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PositionEvent>(e =>
        {
            e.Property(x => x.TxHash).HasMaxLength(100);
            e.Property(x => x.RewardTokenAddress).HasMaxLength(64);
            e.Property(x => x.RewardTokenSymbol).HasMaxLength(64);
            e.HasIndex(x => new { x.PositionId, x.TxHash, x.LogIndex }).IsUnique();
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<PositionSnapshot>(e =>
        {
            e.HasIndex(x => new { x.PositionId, x.Timestamp });
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TokenPrice>(e =>
        {
            e.Property(x => x.PriceKey).HasMaxLength(100);
            e.HasIndex(x => new { x.PriceKey, x.Timestamp }).IsUnique();
        });

        modelBuilder.Entity<SyncCursor>(e =>
        {
            e.Property(x => x.Owner).HasMaxLength(64);
            e.HasIndex(x => new { x.Chain, x.Protocol, x.Owner }).IsUnique();
        });

        ApplyGlobalConverters(modelBuilder);
    }

    private static void ApplyGlobalConverters(ModelBuilder modelBuilder)
    {
        var bigInteger = new ValueConverter<BigInteger, string>(v => v.ToString(), v => BigInteger.Parse(v));
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (type == typeof(BigInteger)) property.SetValueConverter(bigInteger);
            else if (type == typeof(DateTime)) property.SetValueConverter(utc);
        }
    }
}
