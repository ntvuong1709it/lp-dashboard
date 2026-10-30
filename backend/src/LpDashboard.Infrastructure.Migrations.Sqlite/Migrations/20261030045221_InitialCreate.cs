using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LpDashboard.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastSyncedBlock = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TokenPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PriceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    PriceUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Decimals = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchedAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ChainFamily = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedAddresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Token0Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Token1Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Fee = table.Column<int>(type: "INTEGER", nullable: true),
                    TickSpacing = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pools_Tokens_Token0Id",
                        column: x => x.Token0Id,
                        principalTable: "Tokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pools_Tokens_Token1Id",
                        column: x => x.Token1Id,
                        principalTable: "Tokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PositionManager = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TokenId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PoolId = table.Column<int>(type: "INTEGER", nullable: true),
                    TickLower = table.Column<int>(type: "INTEGER", nullable: false),
                    TickUpper = table.Column<int>(type: "INTEGER", nullable: false),
                    Liquidity = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsStaked = table.Column<bool>(type: "INTEGER", nullable: false),
                    Gauge = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OpenedBlock = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentTick = table.Column<int>(type: "INTEGER", nullable: true),
                    SqrtPriceX96 = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CurrentAmount0 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    CurrentAmount1 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Uncollected0 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Uncollected1 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    PendingReward = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    RewardTokenAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RewardTokenSymbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Price0Usd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    Price1Usd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    RewardPriceUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    ValuedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Positions_Pools_PoolId",
                        column: x => x.PoolId,
                        principalTable: "Pools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PositionEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PositionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TxHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LogIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    BlockNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount0 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Amount1 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Principal0 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Principal1 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Fee0 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Fee1 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    RewardTokenAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RewardTokenSymbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RewardAmount = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Price0Usd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    Price1Usd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    RewardPriceUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    GasNative = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    GasUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionEvents_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PositionSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PositionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BlockNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentTick = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount0 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Amount1 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Uncollected0 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Uncollected1 = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    PendingReward = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    Price0Usd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    Price1Usd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    RewardPriceUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    ValueUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    UncollectedUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionSnapshots_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pools_Chain_Address",
                table: "Pools",
                columns: new[] { "Chain", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pools_Token0Id",
                table: "Pools",
                column: "Token0Id");

            migrationBuilder.CreateIndex(
                name: "IX_Pools_Token1Id",
                table: "Pools",
                column: "Token1Id");

            migrationBuilder.CreateIndex(
                name: "IX_PositionEvents_PositionId_TxHash_LogIndex",
                table: "PositionEvents",
                columns: new[] { "PositionId", "TxHash", "LogIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PositionEvents_Timestamp",
                table: "PositionEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Chain_PositionManager_TokenId",
                table: "Positions",
                columns: new[] { "Chain", "PositionManager", "TokenId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Owner",
                table: "Positions",
                column: "Owner");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_PoolId",
                table: "Positions",
                column: "PoolId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionSnapshots_PositionId_Timestamp",
                table: "PositionSnapshots",
                columns: new[] { "PositionId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncCursors_Chain_Protocol_Owner",
                table: "SyncCursors",
                columns: new[] { "Chain", "Protocol", "Owner" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenPrices_PriceKey_Timestamp",
                table: "TokenPrices",
                columns: new[] { "PriceKey", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_Chain_Address",
                table: "Tokens",
                columns: new[] { "Chain", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchedAddresses_Address",
                table: "WatchedAddresses",
                column: "Address",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionEvents");

            migrationBuilder.DropTable(
                name: "PositionSnapshots");

            migrationBuilder.DropTable(
                name: "SyncCursors");

            migrationBuilder.DropTable(
                name: "TokenPrices");

            migrationBuilder.DropTable(
                name: "WatchedAddresses");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "Pools");

            migrationBuilder.DropTable(
                name: "Tokens");
        }
    }
}
