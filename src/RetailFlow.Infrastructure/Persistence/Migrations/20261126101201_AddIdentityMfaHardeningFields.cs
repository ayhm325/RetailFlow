using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityMfaHardeningFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastUsedTotpTimeStep",
                table: "AspNetUsers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MfaEnrollmentStatus",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MfaFailedAttempts",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MfaLockoutEndUtc",
                table: "AspNetUsers",
                type: "datetimeoffset(3)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MfaPendingExpiresAtUtc",
                table: "AspNetUsers",
                type: "datetimeoffset(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedPendingAuthenticatorKey",
                table: "AspNetUsers",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastUsedTotpTimeStep",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MfaEnrollmentStatus",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MfaFailedAttempts",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MfaLockoutEndUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MfaPendingExpiresAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProtectedPendingAuthenticatorKey",
                table: "AspNetUsers");
        }
    }
}
