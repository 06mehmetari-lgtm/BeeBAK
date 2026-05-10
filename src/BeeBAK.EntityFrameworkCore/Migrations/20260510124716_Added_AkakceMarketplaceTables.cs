using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeBAK.Migrations
{
    /// <inheritdoc />
    public partial class Added_AkakceMarketplaceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppAkakceMerchants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
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
                    table.PrimaryKey("PK_AppAkakceMerchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppAkakceProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BrandName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PrimaryImageUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CategoryPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    BestPriceAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    PreviousPriceAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    OfferCount = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppAkakceProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppAkakceOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    OfferTitle = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SellerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ShippingText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    StockText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DeliveryText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LastUpdatedText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OfferUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    MerchantProductUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ScrapedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAkakceOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAkakceOffers_AppAkakceMerchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "AppAkakceMerchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppAkakceOffers_AppAkakceProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "AppAkakceProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAkakceMerchants_Slug",
                table: "AppAkakceMerchants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppAkakceOffers_MerchantId",
                table: "AppAkakceOffers",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAkakceOffers_ProductId_DisplayOrder",
                table: "AppAkakceOffers",
                columns: new[] { "ProductId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAkakceProducts_LastSyncedUtc",
                table: "AppAkakceProducts",
                column: "LastSyncedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppAkakceProducts_ProductCode",
                table: "AppAkakceProducts",
                column: "ProductCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAkakceOffers");

            migrationBuilder.DropTable(
                name: "AppAkakceMerchants");

            migrationBuilder.DropTable(
                name: "AppAkakceProducts");
        }
    }
}
