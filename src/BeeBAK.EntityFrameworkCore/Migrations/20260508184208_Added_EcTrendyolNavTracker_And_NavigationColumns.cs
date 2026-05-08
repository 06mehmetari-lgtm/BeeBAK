using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeBAK.Migrations
{
    /// <inheritdoc />
    public partial class Added_EcTrendyolNavTracker_And_NavigationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastNavigationSyncUtc",
                table: "AppEcMarketplaceCategories",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NavigationDisplayOrder",
                table: "AppEcMarketplaceCategories",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppEcTrendyolNavSectionTrackers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalCategoryId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEcTrendyolNavSectionTrackers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEcTrendyolNavSectionTrackers_ExternalCategoryId",
                table: "AppEcTrendyolNavSectionTrackers",
                column: "ExternalCategoryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEcTrendyolNavSectionTrackers");

            migrationBuilder.DropColumn(
                name: "LastNavigationSyncUtc",
                table: "AppEcMarketplaceCategories");

            migrationBuilder.DropColumn(
                name: "NavigationDisplayOrder",
                table: "AppEcMarketplaceCategories");
        }
    }
}
