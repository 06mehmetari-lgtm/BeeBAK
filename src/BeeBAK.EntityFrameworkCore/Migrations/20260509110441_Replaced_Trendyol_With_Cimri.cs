using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeBAK.Migrations
{
    /// <inheritdoc />
    public partial class Replaced_Trendyol_With_Cimri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEcTrendyolNavSectionTrackers");

            migrationBuilder.CreateTable(
                name: "AppCimriMerchants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExternalMerchantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_AppCimriMerchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCimriProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PrimaryCategorySlug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CategoryPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BrandName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PrimaryImageUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    TotalOfferCount = table.Column<int>(type: "integer", nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    BestPriceAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    BestPriceMerchantName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PreviousPriceAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
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
                    table.PrimaryKey("PK_AppCimriProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCimriOffers",
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
                    PromotionText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LastUpdatedText = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    InstallmentBadge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MerchantScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    IsSponsored = table.Column<bool>(type: "boolean", nullable: false),
                    IsCheapest = table.Column<bool>(type: "boolean", nullable: false),
                    YearsOnCimri = table.Column<int>(type: "integer", nullable: true),
                    OfferUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ScrapedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCimriOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCimriOffers_AppCimriMerchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "AppCimriMerchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppCimriOffers_AppCimriProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "AppCimriProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCimriMerchants_Slug",
                table: "AppCimriMerchants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCimriOffers_MerchantId",
                table: "AppCimriOffers",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCimriOffers_ProductId_DisplayOrder",
                table: "AppCimriOffers",
                columns: new[] { "ProductId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCimriProducts_ContentId",
                table: "AppCimriProducts",
                column: "ContentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCimriProducts_LastSyncedUtc",
                table: "AppCimriProducts",
                column: "LastSyncedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppCimriProducts_PrimaryCategorySlug",
                table: "AppCimriProducts",
                column: "PrimaryCategorySlug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCimriOffers");

            migrationBuilder.DropTable(
                name: "AppCimriMerchants");

            migrationBuilder.DropTable(
                name: "AppCimriProducts");

            migrationBuilder.CreateTable(
                name: "AppEcTrendyolNavSectionTrackers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExternalCategoryId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
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
    }
}
