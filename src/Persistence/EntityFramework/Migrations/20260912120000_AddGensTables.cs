using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddGensTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GensEntry",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GensType = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Family = table.Column<int>(type: "smallint", nullable: false),
                    ContributionPoints = table.Column<long>(type: "bigint", nullable: false),
                    JoinDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GensEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GensEntry_Character_Id",
                        column: x => x.Id,
                        principalSchema: "data",
                        principalTable: "Character",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GensEntry_GensType",
                schema: "data",
                table: "GensEntry",
                column: "GensType");

            migrationBuilder.CreateIndex(
                name: "IX_GensEntry_ContributionPoints",
                schema: "data",
                table: "GensEntry",
                column: "ContributionPoints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GensEntry",
                schema: "data");
        }
    }
}