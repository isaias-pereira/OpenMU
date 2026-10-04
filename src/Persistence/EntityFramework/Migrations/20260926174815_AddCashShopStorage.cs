using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCashShopStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashShopStorageItem",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageIndex = table.Column<int>(type: "integer", nullable: false),
                    ItemSeq = table.Column<int>(type: "integer", nullable: false),
                    StorageGroup = table.Column<int>(type: "integer", nullable: false),
                    ProductSeq = table.Column<int>(type: "integer", nullable: false),
                    PriceSeq = table.Column<int>(type: "integer", nullable: false),
                    CashPoint = table.Column<double>(type: "double precision", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShopStorageItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashShopStorageItem_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "data",
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CashShopStorageItem_Item_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "data",
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashShopStorageItem_AccountId",
                schema: "data",
                table: "CashShopStorageItem",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopStorageItem_AccountId_StorageIndex",
                schema: "data",
                table: "CashShopStorageItem",
                columns: new[] { "AccountId", "StorageIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShopStorageItem_ItemId",
                schema: "data",
                table: "CashShopStorageItem",
                column: "ItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashShopStorageItem",
                schema: "data");
        }
    }
}
