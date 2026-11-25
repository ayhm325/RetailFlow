using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAbsoluteSessionExpiration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AbsoluteExpiresUtc",
                table: "AuthenticationSessionTickets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.Sql(
                """
                EXEC(N'
                    UPDATE [AuthenticationSessionTickets]
                    SET [AbsoluteExpiresUtc] = DATEADD(HOUR, 8, [CreatedUtc])
                    WHERE [AbsoluteExpiresUtc] IS NULL;
                ');
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "AbsoluteExpiresUtc",
                table: "AuthenticationSessionTickets",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessionTickets_AbsoluteExpiresUtc",
                table: "AuthenticationSessionTickets",
                column: "AbsoluteExpiresUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthenticationSessionTickets_AbsoluteExpiresUtc",
                table: "AuthenticationSessionTickets");

            migrationBuilder.DropColumn(
                name: "AbsoluteExpiresUtc",
                table: "AuthenticationSessionTickets");
        }
    }
}
