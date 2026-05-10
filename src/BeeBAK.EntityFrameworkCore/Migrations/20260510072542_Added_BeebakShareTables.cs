using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeBAK.Migrations
{
    /// <inheritdoc />
    public partial class Added_BeebakShareTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppBeebakShareCardLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CardPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ProductFingerprint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppBeebakShareCardLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppBeebakShareProductDayBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CimriContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BlockUtcDate = table.Column<DateTime>(type: "date", nullable: false),
                    ChannelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppBeebakShareProductDayBlocks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppBeebakShareCardLogs_CreatedUtc_ProductFingerprint",
                table: "AppBeebakShareCardLogs",
                columns: new[] { "CreatedUtc", "ProductFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_AppBeebakShareProductDayBlocks_CimriContentId_BlockUtcDate_~",
                table: "AppBeebakShareProductDayBlocks",
                columns: new[] { "CimriContentId", "BlockUtcDate", "ChannelName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppBeebakShareCardLogs");

            migrationBuilder.DropTable(
                name: "AppBeebakShareProductDayBlocks");
        }
    }
}
