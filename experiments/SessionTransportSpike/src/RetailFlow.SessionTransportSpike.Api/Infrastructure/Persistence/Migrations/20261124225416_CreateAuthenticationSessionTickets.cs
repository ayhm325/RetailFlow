using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateAuthenticationSessionTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthenticationSessionTickets",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AccountKind = table.Column<int>(type: "int", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProtectedTicket = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastRenewedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticationSessionTickets", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessionTickets_ExpiresUtc",
                table: "AuthenticationSessionTickets",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessionTickets_UserId_StoreId_AccountKind",
                table: "AuthenticationSessionTickets",
                columns: new[] { "UserId", "StoreId", "AccountKind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticationSessionTickets");
        }
    }
}
