using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeBAK.Migrations
{
    /// <inheritdoc />
    public partial class AddEcommerceMarketplaceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppEcMarketplaceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Marketplace = table.Column<int>(type: "integer", nullable: false),
                    ExternalCategoryId = table.Column<string>(type: "text", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: true),
                    CategoryUrl = table.Column<string>(type: "text", nullable: true),
                    ExtraAttributesJson = table.Column<string>(type: "jsonb", nullable: true),
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
                    table.PrimaryKey("PK_AppEcMarketplaceCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppEcMarketplaceCategories_AppEcMarketplaceCategories_Paren~",
                        column: x => x.ParentId,
                        principalTable: "AppEcMarketplaceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppEcScrapeRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Marketplace = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TriggerSource = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    StatisticsJson = table.Column<string>(type: "jsonb", nullable: true),
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
                    table.PrimaryKey("PK_AppEcScrapeRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppEcProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Marketplace = table.Column<int>(type: "integer", nullable: false),
                    ExternalProductId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    BrandName = table.Column<string>(type: "text", nullable: true),
                    PrimaryCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductUrl = table.Column<string>(type: "text", nullable: false),
                    Barcode = table.Column<string>(type: "text", nullable: true),
                    MerchantExternalId = table.Column<string>(type: "text", nullable: true),
                    LastSyncedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AppEcProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppEcProducts_AppEcMarketplaceCategories_PrimaryCategoryId",
                        column: x => x.PrimaryCategoryId,
                        principalTable: "AppEcMarketplaceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AppEcProductDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    DescriptionHtml = table.Column<string>(type: "text", nullable: true),
                    SpecificationsJson = table.Column<string>(type: "jsonb", nullable: true),
                    RatingAverage = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    ReviewCount = table.Column<int>(type: "integer", nullable: true),
                    SellerName = table.Column<string>(type: "text", nullable: true),
                    SellerScoreJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEcProductDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppEcProductDetails_AppEcProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "AppEcProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppEcProductImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEcProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppEcProductImages_AppEcProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "AppEcProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppEcProductPriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    ListPriceAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DiscountPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    ScrapedUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RawOfferJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEcProductPriceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppEcProductPriceSnapshots_AppEcProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "AppEcProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppEcMarketplaceCategories_Marketplace_ExternalCategoryId",
                table: "AppEcMarketplaceCategories",
                columns: new[] { "Marketplace", "ExternalCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEcMarketplaceCategories_ParentId",
                table: "AppEcMarketplaceCategories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEcProductDetails_ProductId",
                table: "AppEcProductDetails",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEcProductImages_ProductId",
                table: "AppEcProductImages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEcProductPriceSnapshots_ProductId",
                table: "AppEcProductPriceSnapshots",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AppEcProducts_Marketplace_ExternalProductId",
                table: "AppEcProducts",
                columns: new[] { "Marketplace", "ExternalProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEcProducts_PrimaryCategoryId",
                table: "AppEcProducts",
                column: "PrimaryCategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEcProductDetails");

            migrationBuilder.DropTable(
                name: "AppEcProductImages");

            migrationBuilder.DropTable(
                name: "AppEcProductPriceSnapshots");

            migrationBuilder.DropTable(
                name: "AppEcScrapeRuns");

            migrationBuilder.DropTable(
                name: "AppEcProducts");

            migrationBuilder.DropTable(
                name: "AppEcMarketplaceCategories");
        }
    }
}
