using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LpDashboard.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddLending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LendingAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TotalCollateralUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    TotalDebtUsd = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    LiquidationThreshold = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    HealthFactor = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HistoryThrough = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    HistoryFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    HistoryNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LendingAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LendingDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reserve = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SuppliedTokens = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    BorrowedTokens = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    YieldTokens = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    InterestTokens = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    SupplyIndex = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    BorrowIndex = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    IsCollateral = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LendingDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LendingReserves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Reserve = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Decimals = table.Column<int>(type: "INTEGER", nullable: false),
                    PriceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SuppliedTokens = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    BorrowedTokens = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    SupplyAprPercent = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    BorrowAprPercent = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    IsCollateral = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LendingReserves", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LendingAccounts_Chain_Owner",
                table: "LendingAccounts",
                columns: new[] { "Chain", "Owner" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LendingDays_Chain_Owner_Reserve_Date",
                table: "LendingDays",
                columns: new[] { "Chain", "Owner", "Reserve", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LendingReserves_Chain_Owner_Reserve",
                table: "LendingReserves",
                columns: new[] { "Chain", "Owner", "Reserve" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LendingAccounts");

            migrationBuilder.DropTable(
                name: "LendingDays");

            migrationBuilder.DropTable(
                name: "LendingReserves");
        }
    }
}
