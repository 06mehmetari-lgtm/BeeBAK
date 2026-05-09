using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeBAK.Migrations
{
    /// <inheritdoc />
    public partial class Added_CimriOffer_MerchantProductInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MerchantProductId",
                table: "AppCimriOffers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchantProductUrl",
                table: "AppCimriOffers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCimriOffers_MerchantProductId",
                table: "AppCimriOffers",
                column: "MerchantProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppCimriOffers_MerchantProductId",
                table: "AppCimriOffers");

            migrationBuilder.DropColumn(
                name: "MerchantProductId",
                table: "AppCimriOffers");

            migrationBuilder.DropColumn(
                name: "MerchantProductUrl",
                table: "AppCimriOffers");
        }
    }
}
