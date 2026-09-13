using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomMoveTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomMoveEntry",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    MapId = table.Column<Guid>(type: "uuid", nullable: true),
                    Command = table.Column<string>(type: "text", nullable: true),
                    X = table.Column<short>(type: "smallint", nullable: false),
                    Y = table.Column<short>(type: "smallint", nullable: false),
                    Cost = table.Column<int>(type: "integer", nullable: false),
                    MinLevel = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomMoveEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomMoveEntry_GameMapDefinition_MapId",
                        column: x => x.MapId,
                        principalSchema: "config",
                        principalTable: "GameMapDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomMoveEntry_MapId",
                schema: "data",
                table: "CustomMoveEntry",
                column: "MapId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomMoveEntry_Name",
                schema: "data",
                table: "CustomMoveEntry",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomMoveEntry",
                schema: "data");
        }
    }
}
