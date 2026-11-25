using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenCurrencyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Currencies_Code_Format",
                table: "Currencies");

            migrationBuilder.CreateIndex(
                name: "UX_Currencies_NumericCode",
                table: "Currencies",
                column: "NumericCode",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Currencies_Code_Format",
                table: "Currencies",
                sql: "LEN([Code]) = 3 AND [Code] COLLATE Latin1_General_BIN2 NOT LIKE '%[^A-Z]%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Currencies_NumericCode",
                table: "Currencies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Currencies_Code_Format",
                table: "Currencies");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Currencies_Code_Format",
                table: "Currencies",
                sql: "LEN([Code]) = 3 AND [Code] NOT LIKE '%[^A-Z]%'");
        }
    }
}
