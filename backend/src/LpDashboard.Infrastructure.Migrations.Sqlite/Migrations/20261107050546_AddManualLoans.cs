using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LpDashboard.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddManualLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManualLoans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AssetSymbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AssetPriceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CollateralSymbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CollateralPriceKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MarginCallLtvPercent = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    LiquidationLtvPercent = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    ClosedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualLoans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManualLoanCollateral",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LoanId = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualLoanCollateral", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualLoanCollateral_ManualLoans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "ManualLoans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManualLoanTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LoanId = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Principal = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    AprPercent = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualLoanTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualLoanTerms_ManualLoans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "ManualLoans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualLoanCollateral_LoanId",
                table: "ManualLoanCollateral",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_ManualLoans_Owner",
                table: "ManualLoans",
                column: "Owner");

            migrationBuilder.CreateIndex(
                name: "IX_ManualLoanTerms_LoanId",
                table: "ManualLoanTerms",
                column: "LoanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManualLoanCollateral");

            migrationBuilder.DropTable(
                name: "ManualLoanTerms");

            migrationBuilder.DropTable(
                name: "ManualLoans");
        }
    }
}
