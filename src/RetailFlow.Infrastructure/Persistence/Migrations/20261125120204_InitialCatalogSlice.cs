using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalogSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StoreType = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                    table.CheckConstraint("CK_Stores_Status", "[Status] IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_Stores_StoreType", "[StoreType] IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.UniqueConstraint("AK_Categories_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.ForeignKey(
                        name: "FK_Categories_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.UniqueConstraint("AK_Products_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_Products_Price_NonNegative", "[Price] >= 0");
                    table.CheckConstraint("CK_Products_Status", "[Status] IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_Products_Categories_StoreId_CategoryId",
                        columns: x => new { x.StoreId, x.CategoryId },
                        principalTable: "Categories",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnHand = table.Column<int>(type: "int", nullable: false),
                    Reserved = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryItems", x => x.Id);
                    table.CheckConstraint("CK_InventoryItems_OnHand_NonNegative", "[OnHand] >= 0");
                    table.CheckConstraint("CK_InventoryItems_Reserved_NonNegative", "[Reserved] >= 0");
                    table.CheckConstraint("CK_InventoryItems_Reserved_NotExceed_OnHand", "[Reserved] <= [OnHand]");
                    table.ForeignKey(
                        name: "FK_InventoryItems_Products_StoreId_ProductId",
                        columns: x => new { x.StoreId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImages_Products_StoreId_ProductId",
                        columns: x => new { x.StoreId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_StoreId_IsActive_SortOrder_Id",
                table: "Categories",
                columns: new[] { "StoreId", "IsActive", "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Categories_StoreId_Slug",
                table: "Categories",
                columns: new[] { "StoreId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryItems_StoreId_ProductId",
                table: "InventoryItems",
                columns: new[] { "StoreId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_StoreId_ProductId_SortOrder_Id",
                table: "ProductImages",
                columns: new[] { "StoreId", "ProductId", "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId_CategoryId",
                table: "Products",
                columns: new[] { "StoreId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId_Status_CategoryId",
                table: "Products",
                columns: new[] { "StoreId", "Status", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId_Status_CreatedAtUtc_Id",
                table: "Products",
                columns: new[] { "StoreId", "Status", "CreatedAtUtc", "Id" },
                descending: new[] { false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId_Status_Price_Id",
                table: "Products",
                columns: new[] { "StoreId", "Status", "Price", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_Products_StoreId_Sku",
                table: "Products",
                columns: new[] { "StoreId", "Sku" },
                unique: true,
                filter: "[Sku] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Stores_Slug",
                table: "Stores",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryItems");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}
