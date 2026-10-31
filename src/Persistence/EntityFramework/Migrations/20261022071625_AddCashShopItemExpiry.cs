using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCashShopItemExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAtUtc",
                schema: "data",
                table: "Item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                schema: "data",
                table: "Item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                schema: "data",
                table: "CashShopStorageItem",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                schema: "data",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                schema: "data",
                table: "Item");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                schema: "data",
                table: "CashShopStorageItem");
        }
    }
}
