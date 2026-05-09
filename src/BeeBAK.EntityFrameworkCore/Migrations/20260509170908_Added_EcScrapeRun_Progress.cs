using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeBAK.Migrations
{
    /// <inheritdoc />
    public partial class Added_EcScrapeRun_Progress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelRequested",
                table: "AppEcScrapeRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FailedItems",
                table: "AppEcScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedItems",
                table: "AppEcScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalItems",
                table: "AppEcScrapeRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelRequested",
                table: "AppEcScrapeRuns");

            migrationBuilder.DropColumn(
                name: "FailedItems",
                table: "AppEcScrapeRuns");

            migrationBuilder.DropColumn(
                name: "ProcessedItems",
                table: "AppEcScrapeRuns");

            migrationBuilder.DropColumn(
                name: "TotalItems",
                table: "AppEcScrapeRuns");
        }
    }
}
