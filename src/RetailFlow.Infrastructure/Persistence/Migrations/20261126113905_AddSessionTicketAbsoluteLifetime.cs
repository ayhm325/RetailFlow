using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTicketAbsoluteLifetime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AbsoluteExpiresAtUtc",
                table: "SessionTickets",
                type: "datetimeoffset(3)",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "SessionTickets",
                type: "datetimeoffset(3)",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_SessionTickets_AbsoluteExpiresAtUtc",
                table: "SessionTickets",
                column: "AbsoluteExpiresAtUtc");

            migrationBuilder.Sql("""
                UPDATE [SessionTickets]
                SET [CreatedAtUtc] = [LastSeenAtUtc],
                    [AbsoluteExpiresAtUtc] = CASE
                        WHEN [ExpiresAtUtc] > DATEADD(hour, 12, [LastSeenAtUtc]) THEN DATEADD(hour, 12, [LastSeenAtUtc])
                        ELSE [ExpiresAtUtc]
                    END
                WHERE [AbsoluteExpiresAtUtc] = '0001-01-01T00:00:00.000+00:00';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionTickets_AbsoluteExpiresAtUtc",
                table: "SessionTickets");

            migrationBuilder.DropColumn(
                name: "AbsoluteExpiresAtUtc",
                table: "SessionTickets");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "SessionTickets");
        }
    }
}
