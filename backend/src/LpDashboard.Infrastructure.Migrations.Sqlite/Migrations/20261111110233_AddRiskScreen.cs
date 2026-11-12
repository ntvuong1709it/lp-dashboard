using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LpDashboard.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskScreen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PoolLiquiditySnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PoolAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Token0Raw = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Token1Raw = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolLiquiditySnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskAlertStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FirstAlertedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskAlertStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExplorerId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsLatest = table.Column<bool>(type: "INTEGER", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ChecksRun = table.Column<int>(type: "INTEGER", nullable: false),
                    ChecksTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    FindingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    FactsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourcesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ChangesJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StarredPools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Chain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExplorerId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Token0 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Token1 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PoolAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StarredPools", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoolLiquiditySnapshots_Chain_PoolAddress_At",
                table: "PoolLiquiditySnapshots",
                columns: new[] { "Chain", "PoolAddress", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskAlertStates_Kind_Chain_Subject_Code_ClearedAt",
                table: "RiskAlertStates",
                columns: new[] { "Kind", "Chain", "Subject", "Code", "ClearedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskChecks_CheckedAt",
                table: "RiskChecks",
                column: "CheckedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RiskChecks_Kind_Chain_ExplorerId_IsLatest",
                table: "RiskChecks",
                columns: new[] { "Kind", "Chain", "ExplorerId", "IsLatest" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskChecks_Kind_Chain_Subject_IsLatest",
                table: "RiskChecks",
                columns: new[] { "Kind", "Chain", "Subject", "IsLatest" });

            migrationBuilder.CreateIndex(
                name: "IX_StarredPools_Chain_ExplorerId",
                table: "StarredPools",
                columns: new[] { "Chain", "ExplorerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoolLiquiditySnapshots");

            migrationBuilder.DropTable(
                name: "RiskAlertStates");

            migrationBuilder.DropTable(
                name: "RiskChecks");

            migrationBuilder.DropTable(
                name: "StarredPools");
        }
    }
}
