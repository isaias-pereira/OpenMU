using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class FixCashShopCatalogNaturalKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashShopProduct_PriceSeq",
                schema: "data",
                table: "CashShopProduct");

            migrationBuilder.DropIndex(
                name: "IX_CashShopPackage_PackageProductSeq",
                schema: "data",
                table: "CashShopPackage");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopProduct_PriceSeq",
                schema: "data",
                table: "CashShopProduct",
                column: "PriceSeq");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopProduct_ProductSeq_PriceSeq_PropertySeq",
                schema: "data",
                table: "CashShopProduct",
                columns: new[] { "ProductSeq", "PriceSeq", "PropertySeq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShopPackage_PackageProductSeq",
                schema: "data",
                table: "CashShopPackage",
                column: "PackageProductSeq");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopPackage_ProductDisplaySeq_PackageProductSeq",
                schema: "data",
                table: "CashShopPackage",
                columns: new[] { "ProductDisplaySeq", "PackageProductSeq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashShopProduct_PriceSeq",
                schema: "data",
                table: "CashShopProduct");

            migrationBuilder.DropIndex(
                name: "IX_CashShopProduct_ProductSeq_PriceSeq_PropertySeq",
                schema: "data",
                table: "CashShopProduct");

            migrationBuilder.DropIndex(
                name: "IX_CashShopPackage_PackageProductSeq",
                schema: "data",
                table: "CashShopPackage");

            migrationBuilder.DropIndex(
                name: "IX_CashShopPackage_ProductDisplaySeq_PackageProductSeq",
                schema: "data",
                table: "CashShopPackage");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopProduct_PriceSeq",
                schema: "data",
                table: "CashShopProduct",
                column: "PriceSeq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShopPackage_PackageProductSeq",
                schema: "data",
                table: "CashShopPackage",
                column: "PackageProductSeq",
                unique: true);
        }
    }
}
