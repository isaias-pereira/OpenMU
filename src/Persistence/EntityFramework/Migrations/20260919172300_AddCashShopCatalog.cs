using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCashShopCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tabela de Categorias
            migrationBuilder.CreateTable(
                name: "CashShopCategory",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductDisplaySeq = table.Column<int>(type: "integer", nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventFlag = table.Column<int>(type: "integer", nullable: false),
                    OpenFlag = table.Column<int>(type: "integer", nullable: false),
                    ParentProductDisplaySeq = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsRoot = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShopCategory", x => x.Id);
                });

            // Tabela de Pacotes
            migrationBuilder.CreateTable(
                name: "CashShopPackage",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductDisplaySeq = table.Column<int>(type: "integer", nullable: false),
                    ViewOrder = table.Column<int>(type: "integer", nullable: false),
                    PackageProductSeq = table.Column<int>(type: "integer", nullable: false),
                    PackageProductName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PackageProductType = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Caution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SalesFlag = table.Column<int>(type: "integer", nullable: false),
                    GiftFlag = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CapsuleFlag = table.Column<int>(type: "integer", nullable: false),
                    CapsuleCount = table.Column<int>(type: "integer", nullable: false),
                    ProductCashName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PriceUnitName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeleteFlag = table.Column<int>(type: "integer", nullable: false),
                    EventFlag = table.Column<int>(type: "integer", nullable: false),
                    ProductAmount = table.Column<int>(type: "integer", nullable: false),
                    ProductSeqList = table.Column<string>(type: "text", nullable: false),
                    InGamePackageID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductCashSeq = table.Column<int>(type: "integer", nullable: false),
                    PriceCount = table.Column<int>(type: "integer", nullable: false),
                    PriceSeqList = table.Column<string>(type: "text", nullable: false),
                    DeductMileageFlag = table.Column<bool>(type: "boolean", nullable: false),
                    CashType = table.Column<int>(type: "integer", nullable: false),
                    CashTypeFlag = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShopPackage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashShopPackage_CashShopCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "data",
                        principalTable: "CashShopCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Tabela de Produtos
            migrationBuilder.CreateTable(
                name: "CashShopProduct",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductSeq = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PropertyName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UnitName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Price = table.Column<int>(type: "integer", nullable: false),
                    PriceSeq = table.Column<int>(type: "integer", nullable: false),
                    PropertyType = table.Column<int>(type: "integer", nullable: false),
                    MustFlag = table.Column<bool>(type: "boolean", nullable: false),
                    ViewOrder = table.Column<int>(type: "integer", nullable: false),
                    DeleteFlag = table.Column<int>(type: "integer", nullable: false),
                    StorageGroup = table.Column<int>(type: "integer", nullable: false),
                    ShareFlag = table.Column<bool>(type: "boolean", nullable: false),
                    InGamePackageID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PropertySeq = table.Column<int>(type: "integer", nullable: false),
                    ProductType = table.Column<int>(type: "integer", nullable: false),
                    UnitType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShopProduct", x => x.Id);
                });

            // Tabela de Junção (M:N)
            migrationBuilder.CreateTable(
                name: "CashShopPackageProduct",
                schema: "data",
                columns: table => new
                {
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShopPackageProduct", x => new { x.PackageId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_CashShopPackageProduct_CashShopPackage_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "data",
                        principalTable: "CashShopPackage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CashShopPackageProduct_CashShopProduct_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "data",
                        principalTable: "CashShopProduct",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Índices e Constraints
            migrationBuilder.CreateIndex(
                name: "IX_CashShopCategory_ProductDisplaySeq",
                schema: "data",
                table: "CashShopCategory",
                column: "ProductDisplaySeq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShopPackage_PackageProductSeq",
                schema: "data",
                table: "CashShopPackage",
                column: "PackageProductSeq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShopPackage_CategoryId",
                schema: "data",
                table: "CashShopPackage",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopProduct_PriceSeq",
                schema: "data",
                table: "CashShopProduct",
                column: "PriceSeq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShopProduct_ProductSeq",
                schema: "data",
                table: "CashShopProduct",
                column: "ProductSeq");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopProduct_InGamePackageID",
                schema: "data",
                table: "CashShopProduct",
                column: "InGamePackageID");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopPackageProduct_ProductId",
                schema: "data",
                table: "CashShopPackageProduct",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashShopPackageProduct",
                schema: "data");

            migrationBuilder.DropTable(
                name: "CashShopProduct",
                schema: "data");

            migrationBuilder.DropTable(
                name: "CashShopPackage",
                schema: "data");

            migrationBuilder.DropTable(
                name: "CashShopCategory",
                schema: "data");
        }
    }
}
