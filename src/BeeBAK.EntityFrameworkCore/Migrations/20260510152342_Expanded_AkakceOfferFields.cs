using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeeBAK.Migrations
{
    /// <inheritdoc />
    public partial class Expanded_AkakceOfferFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFreeShipping",
                table: "AppAkakceOffers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingAmount",
                table: "AppAkakceOffers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteRedirectUrl",
                table: "AppAkakceOffers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockQuantity",
                table: "AppAkakceOffers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFreeShipping",
                table: "AppAkakceOffers");

            migrationBuilder.DropColumn(
                name: "ShippingAmount",
                table: "AppAkakceOffers");

            migrationBuilder.DropColumn(
                name: "SiteRedirectUrl",
                table: "AppAkakceOffers");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "AppAkakceOffers");
        }
    }
}
