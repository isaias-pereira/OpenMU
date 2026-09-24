using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class RegenerateSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomMoveEntry_GameMapDefinition_MapId",
                schema: "data",
                table: "CustomMoveEntry");

            migrationBuilder.AlterColumn<byte>(
                name: "Rank",
                schema: "data",
                table: "GensEntry",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<byte>(
                name: "GensType",
                schema: "data",
                table: "GensEntry",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentCategoryId",
                schema: "data",
                table: "CashShopCategory",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GameConfigurationId",
                schema: "data",
                table: "CashShopCategory",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GameConfigurationId",
                schema: "data",
                table: "CashShopProduct",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                schema: "data",
                table: "CashShopPackage",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "GameConfigurationId",
                schema: "data",
                table: "CashShopPackage",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                schema: "data",
                table: "CashShopPackageProduct",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_CashShopCategory_GameConfigurationId",
                schema: "data",
                table: "CashShopCategory",
                column: "GameConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopCategory_ParentCategoryId",
                schema: "data",
                table: "CashShopCategory",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopPackage_GameConfigurationId",
                schema: "data",
                table: "CashShopPackage",
                column: "GameConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShopProduct_GameConfigurationId",
                schema: "data",
                table: "CashShopProduct",
                column: "GameConfigurationId");

            migrationBuilder.AddForeignKey(
                name: "FK_CashShopCategory_CashShopCategory_ParentCategoryId",
                schema: "data",
                table: "CashShopCategory",
                column: "ParentCategoryId",
                principalSchema: "data",
                principalTable: "CashShopCategory",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashShopCategory_GameConfiguration_GameConfigurationId",
                schema: "data",
                table: "CashShopCategory",
                column: "GameConfigurationId",
                principalSchema: "config",
                principalTable: "GameConfiguration",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CashShopProduct_GameConfiguration_GameConfigurationId",
                schema: "data",
                table: "CashShopProduct",
                column: "GameConfigurationId",
                principalSchema: "config",
                principalTable: "GameConfiguration",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CashShopPackage_GameConfiguration_GameConfigurationId",
                schema: "data",
                table: "CashShopPackage",
                column: "GameConfigurationId",
                principalSchema: "config",
                principalTable: "GameConfiguration",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomMoveEntry_GameMapDefinition_MapId",
                schema: "data",
                table: "CustomMoveEntry",
                column: "MapId",
                principalSchema: "config",
                principalTable: "GameMapDefinition",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomMoveEntry_GameMapDefinition_MapId",
                schema: "data",
                table: "CustomMoveEntry");

            migrationBuilder.DropForeignKey(
                name: "FK_CashShopPackage_GameConfiguration_GameConfigurationId",
                schema: "data",
                table: "CashShopPackage");

            migrationBuilder.DropForeignKey(
                name: "FK_CashShopProduct_GameConfiguration_GameConfigurationId",
                schema: "data",
                table: "CashShopProduct");

            migrationBuilder.DropForeignKey(
                name: "FK_CashShopCategory_GameConfiguration_GameConfigurationId",
                schema: "data",
                table: "CashShopCategory");

            migrationBuilder.DropForeignKey(
                name: "FK_CashShopCategory_CashShopCategory_ParentCategoryId",
                schema: "data",
                table: "CashShopCategory");

            migrationBuilder.DropIndex(
                name: "IX_CashShopProduct_GameConfigurationId",
                schema: "data",
                table: "CashShopProduct");

            migrationBuilder.DropIndex(
                name: "IX_CashShopPackage_GameConfigurationId",
                schema: "data",
                table: "CashShopPackage");

            migrationBuilder.DropIndex(
                name: "IX_CashShopCategory_ParentCategoryId",
                schema: "data",
                table: "CashShopCategory");

            migrationBuilder.DropIndex(
                name: "IX_CashShopCategory_GameConfigurationId",
                schema: "data",
                table: "CashShopCategory");

            migrationBuilder.DropColumn(
                name: "Id",
                schema: "data",
                table: "CashShopPackageProduct");

            migrationBuilder.DropColumn(
                name: "GameConfigurationId",
                schema: "data",
                table: "CashShopPackage");

            migrationBuilder.DropColumn(
                name: "GameConfigurationId",
                schema: "data",
                table: "CashShopProduct");

            migrationBuilder.DropColumn(
                name: "GameConfigurationId",
                schema: "data",
                table: "CashShopCategory");

            migrationBuilder.DropColumn(
                name: "ParentCategoryId",
                schema: "data",
                table: "CashShopCategory");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                schema: "data",
                table: "CashShopPackage",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "Rank",
                schema: "data",
                table: "GensEntry",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AlterColumn<int>(
                name: "GensType",
                schema: "data",
                table: "GensEntry",
                type: "integer",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "smallint");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomMoveEntry_GameMapDefinition_MapId",
                schema: "data",
                table: "CustomMoveEntry",
                column: "MapId",
                principalSchema: "config",
                principalTable: "GameMapDefinition",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
