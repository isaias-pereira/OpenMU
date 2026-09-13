using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountVaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountVault",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountVault", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountVault_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "data",
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountVault_ItemStorage_StorageId",
                        column: x => x.StorageId,
                        principalSchema: "data",
                        principalTable: "ItemStorage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountVault_AccountId_Number",
                schema: "data",
                table: "AccountVault",
                columns: new[] { "AccountId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountVault_StorageId",
                schema: "data",
                table: "AccountVault",
                column: "StorageId",
                unique: true);

            // Data migration: the legacy single vault (Account.VaultId) becomes vault number 1.
            // Account.Vault stays mapped but inert (snapshot); all runtime access goes through AccountVault.
            migrationBuilder.Sql("""
                INSERT INTO "data"."AccountVault" ("Id", "Number", "AccountId", "StorageId")
                SELECT gen_random_uuid(), 1, a."Id", a."VaultId"
                FROM "data"."Account" a
                WHERE a."VaultId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "data"."AccountVault" v WHERE v."AccountId" = a."Id" AND v."Number" = 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountVault",
                schema: "data");
        }
    }
}
