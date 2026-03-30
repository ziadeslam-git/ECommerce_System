using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECommerce_System.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantImagesAndOrdersUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "ProductVariants");

            migrationBuilder.CreateTable(
                name: "ProductVariantImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PublicId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsMain = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantImages_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantImages_ProductVariantId",
                table: "ProductVariantImages",
                column: "ProductVariantId");

            migrationBuilder.Sql(@"
                IF NOT EXISTS(SELECT 1 FROM Discounts WHERE CouponCode = 'Team2')
                BEGIN
                    INSERT INTO Discounts (CouponCode, Type, Value, MinimumOrderAmount, UsageLimit, UsageCount, ExpiresAt, IsActive)
                    VALUES ('Team2', 'Percentage', 5, NULL, NULL, 0, NULL, 1)
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductVariantImages");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "ProductVariants",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "ProductVariants",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.Sql("DELETE FROM Discounts WHERE CouponCode = 'Team2'");
        }
    }
}
