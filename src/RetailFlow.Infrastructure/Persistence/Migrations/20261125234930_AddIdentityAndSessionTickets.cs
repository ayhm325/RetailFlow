using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityAndSessionTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                    table.CheckConstraint("CK_AppUsers_Role", "[Role] IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "SessionTickets",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Value = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionTickets", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AppUsers_Platform_NormalizedEmail",
                table: "AppUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[StoreId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AppUsers_StoreId_NormalizedEmail",
                table: "AppUsers",
                columns: new[] { "StoreId", "NormalizedEmail" },
                unique: true,
                filter: "[StoreId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SessionTickets_ExpiresAtUtc",
                table: "SessionTickets",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "SessionTickets");
        }
    }
}
