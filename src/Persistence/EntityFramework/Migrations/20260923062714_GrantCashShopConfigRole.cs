using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MUnique.OpenMU.Persistence.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class GrantCashShopConfigRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Grant the configuration role read access to the Cash Shop catalog tables.
            // These tables are part of the GameConfiguration aggregate and are loaded by the
            // "config" role through the ConfigurationContext.
            migrationBuilder.Sql("""
                GRANT SELECT ON TABLE data."CashShopCategory" TO GROUP config;
                GRANT SELECT ON TABLE data."CashShopPackage" TO GROUP config;
                GRANT SELECT ON TABLE data."CashShopPackageProduct" TO GROUP config;
                GRANT SELECT ON TABLE data."CashShopProduct" TO GROUP config;
                GRANT USAGE ON SCHEMA data TO GROUP config;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                REVOKE SELECT ON TABLE data."CashShopCategory" FROM GROUP config;
                REVOKE SELECT ON TABLE data."CashShopPackage" FROM GROUP config;
                REVOKE SELECT ON TABLE data."CashShopPackageProduct" FROM GROUP config;
                REVOKE SELECT ON TABLE data."CashShopProduct" FROM GROUP config;
                """);
        }
    }
}
