using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class AddVipSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VipHistory",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    VipPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VipHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VipHistory_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "data",
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VipPlan",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    DropChanceMultiplier = table.Column<float>(type: "real", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VipPlan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountVip",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VipPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountVip", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountVip_Account_Id",
                        column: x => x.Id,
                        principalSchema: "data",
                        principalTable: "Account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccountVip_VipPlan_VipPlanId",
                        column: x => x.VipPlanId,
                        principalSchema: "data",
                        principalTable: "VipPlan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountVip_VipPlanId",
                schema: "data",
                table: "AccountVip",
                column: "VipPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_VipHistory_AccountId",
                schema: "data",
                table: "VipHistory",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_VipHistory_CreatedAt",
                schema: "data",
                table: "VipHistory",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VipPlan_IsActive",
                schema: "data",
                table: "VipPlan",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountVip",
                schema: "data");

            migrationBuilder.DropTable(
                name: "VipHistory",
                schema: "data");

            migrationBuilder.DropTable(
                name: "VipPlan",
                schema: "data");
        }
    }
}
