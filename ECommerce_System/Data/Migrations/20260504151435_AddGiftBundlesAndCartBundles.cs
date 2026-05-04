using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce_System.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftBundlesAndCartBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CartItems_CartId_ProductVariantId",
                table: "CartItems");

            migrationBuilder.AlterColumn<int>(
                name: "ProductVariantId",
                table: "CartItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "GiftBundleId",
                table: "CartItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GiftBundleItemsJson",
                table: "CartItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GiftBundleOriginalTotal",
                table: "CartItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GiftBundleTitle",
                table: "CartItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GiftBundles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    BundlePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftBundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GiftBundleProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GiftBundleId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftBundleProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiftBundleProducts_GiftBundles_GiftBundleId",
                        column: x => x.GiftBundleId,
                        principalTable: "GiftBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GiftBundleProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId_GiftBundleId",
                table: "CartItems",
                columns: new[] { "CartId", "GiftBundleId" },
                unique: true,
                filter: "[GiftBundleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId_ProductVariantId",
                table: "CartItems",
                columns: new[] { "CartId", "ProductVariantId" },
                unique: true,
                filter: "[ProductVariantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_GiftBundleId",
                table: "CartItems",
                column: "GiftBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftBundleProducts_GiftBundleId_ProductId",
                table: "GiftBundleProducts",
                columns: new[] { "GiftBundleId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GiftBundleProducts_ProductId",
                table: "GiftBundleProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftBundles_IsActive",
                table: "GiftBundles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_GiftBundles_IsFeatured",
                table: "GiftBundles",
                column: "IsFeatured");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_GiftBundles_GiftBundleId",
                table: "CartItems",
                column: "GiftBundleId",
                principalTable: "GiftBundles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_GiftBundles_GiftBundleId",
                table: "CartItems");

            migrationBuilder.DropTable(
                name: "GiftBundleProducts");

            migrationBuilder.DropTable(
                name: "GiftBundles");

            migrationBuilder.DropIndex(
                name: "IX_CartItems_CartId_GiftBundleId",
                table: "CartItems");

            migrationBuilder.DropIndex(
                name: "IX_CartItems_CartId_ProductVariantId",
                table: "CartItems");

            migrationBuilder.DropIndex(
                name: "IX_CartItems_GiftBundleId",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "GiftBundleId",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "GiftBundleItemsJson",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "GiftBundleOriginalTotal",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "GiftBundleTitle",
                table: "CartItems");

            migrationBuilder.AlterColumn<int>(
                name: "ProductVariantId",
                table: "CartItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_CartId_ProductVariantId",
                table: "CartItems",
                columns: new[] { "CartId", "ProductVariantId" },
                unique: true);
        }
    }
}
