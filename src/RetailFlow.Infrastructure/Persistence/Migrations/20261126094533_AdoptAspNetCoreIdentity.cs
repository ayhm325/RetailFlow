using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdoptAspNetCoreIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AppUsers_Platform_NormalizedEmail",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "UX_AppUsers_StoreId_NormalizedEmail",
                table: "AppUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppUsers_Role",
                table: "AppUsers");

            migrationBuilder.RenameTable(
                name: "AppUsers",
                newName: "AspNetUsers");

            migrationBuilder.Sql(
                "IF OBJECT_ID(N'[dbo].[PK_AppUsers]', N'PK') IS NOT NULL EXEC sp_rename N'[dbo].[PK_AppUsers]', N'PK_AspNetUsers', N'OBJECT';");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(512)",
                oldMaxLength: 512);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUserName",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEnd",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LockoutEnabled",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE [AspNetUsers]
                SET [UserName] = LOWER(REPLACE(CONVERT(nvarchar(36), [Id]), '-', '')),
                    [NormalizedUserName] = UPPER(REPLACE(CONVERT(nvarchar(36), [Id]), '-', '')),
                    [SecurityStamp] = UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')),
                    [ConcurrencyStamp] = LOWER(CONVERT(nvarchar(36), NEWID()))
                WHERE [UserName] IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
                VALUES
                    ('11111111-1111-1111-1111-111111111101', N'Customer', N'CUSTOMER', LOWER(CONVERT(nvarchar(36), NEWID()))),
                    ('11111111-1111-1111-1111-111111111102', N'MerchantOwner', N'MERCHANTOWNER', LOWER(CONVERT(nvarchar(36), NEWID()))),
                    ('11111111-1111-1111-1111-111111111103', N'PlatformAdmin', N'PLATFORMADMIN', LOWER(CONVERT(nvarchar(36), NEWID())));

                INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
                SELECT u.[Id],
                       CASE u.[Role]
                           WHEN 1 THEN '11111111-1111-1111-1111-111111111101'
                           WHEN 2 THEN '11111111-1111-1111-1111-111111111102'
                           WHEN 3 THEN '11111111-1111-1111-1111-111111111103'
                       END
                FROM [AspNetUsers] u
                WHERE u.[Role] IN (1, 2, 3);
                """);

            migrationBuilder.DropColumn(
                name: "Role",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AspNetUsers_Platform_NormalizedEmail",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[StoreId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AspNetUsers_StoreId_NormalizedEmail",
                table: "AspNetUsers",
                columns: new[] { "StoreId", "NormalizedEmail" },
                unique: true,
                filter: "[StoreId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UX_AspNetUsers_Platform_NormalizedEmail",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UX_AspNetUsers_StoreId_NormalizedEmail",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                """
                UPDATE u
                SET u.[Role] = CASE r.[NormalizedName]
                    WHEN N'CUSTOMER' THEN 1
                    WHEN N'MERCHANTOWNER' THEN 2
                    WHEN N'PLATFORMADMIN' THEN 3
                    ELSE 1
                END
                FROM [AspNetUsers] u
                INNER JOIN [AspNetUserRoles] ur ON ur.[UserId] = u.[Id]
                INNER JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId];
                """);

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropColumn(name: "UserName", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "NormalizedUserName", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "EmailConfirmed", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "SecurityStamp", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "ConcurrencyStamp", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "PhoneNumber", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "PhoneNumberConfirmed", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "TwoFactorEnabled", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "LockoutEnd", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "LockoutEnabled", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "AccessFailedCount", table: "AspNetUsers");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AspNetUsers",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                newName: "AppUsers");

            migrationBuilder.Sql(
                "IF OBJECT_ID(N'[dbo].[PK_AspNetUsers]', N'PK') IS NOT NULL EXEC sp_rename N'[dbo].[PK_AspNetUsers]', N'PK_AppUsers', N'OBJECT';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppUsers_Role",
                table: "AppUsers",
                sql: "[Role] IN (1, 2, 3)");

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
        }
    }
}
