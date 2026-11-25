using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCartOrdersAndCheckoutIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                    table.UniqueConstraint("AK_Carts_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.ForeignKey(
                        name: "FK_Carts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckoutRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutRequests", x => x.Id);
                    table.CheckConstraint("CK_CheckoutRequests_Status", "[Status] IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_CheckoutRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FulfillmentMode = table.Column<int>(type: "int", nullable: false),
                    OrderStatus = table.Column<int>(type: "int", nullable: false),
                    FulfillmentStatus = table.Column<int>(type: "int", nullable: false),
                    PaymentStatus = table.Column<int>(type: "int", nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DeliveryFee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DeliveryZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeliveryZoneName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DeliveryAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                    ReservationExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.UniqueConstraint("AK_Orders_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_Orders_FulfillmentMode", "[FulfillmentMode] IN (1, 2)");
                    table.CheckConstraint("CK_Orders_FulfillmentStatus", "[FulfillmentStatus] IN (1, 2, 3, 4, 5, 6, 7, 8)");
                    table.CheckConstraint("CK_Orders_OrderStatus", "[OrderStatus] IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_Orders_PaymentStatus", "[PaymentStatus] IN (1, 2, 3)");
                    table.CheckConstraint("CK_Orders_Totals_NonNegative", "[Subtotal] >= 0 AND [DeliveryFee] >= 0 AND [Total] >= 0");
                    table.ForeignKey(
                        name: "FK_Orders_Currencies_CurrencyCode",
                        column: x => x.CurrencyCode,
                        principalTable: "Currencies",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                    table.CheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_CartItems_Carts_StoreId_CartId",
                        columns: x => new { x.StoreId, x.CartId },
                        principalTable: "Carts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.CheckConstraint("CK_OrderItems_Prices_NonNegative", "[UnitPrice] >= 0 AND [LineTotal] >= 0");
                    table.CheckConstraint("CK_OrderItems_Quantity_Positive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_StoreId_OrderId",
                        columns: x => new { x.StoreId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FromOrderStatus = table.Column<int>(type: "int", nullable: true),
                    ToOrderStatus = table.Column<int>(type: "int", nullable: false),
                    FromFulfillmentStatus = table.Column<int>(type: "int", nullable: true),
                    ToFulfillmentStatus = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderStatusHistories_Orders_StoreId_OrderId",
                        columns: x => new { x.StoreId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_CartItems_StoreId_CartId_ProductId",
                table: "CartItems",
                columns: new[] { "StoreId", "CartId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Carts_StoreId_CustomerId",
                table: "Carts",
                columns: new[] { "StoreId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CheckoutRequests_StoreId_CustomerId_IdempotencyKey",
                table: "CheckoutRequests",
                columns: new[] { "StoreId", "CustomerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_StoreId_OrderId",
                table: "OrderItems",
                columns: new[] { "StoreId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CurrencyCode",
                table: "Orders",
                column: "CurrencyCode");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_CustomerId_CreatedAtUtc",
                table: "Orders",
                columns: new[] { "StoreId", "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Orders_StoreId_OrderNumber",
                table: "Orders",
                columns: new[] { "StoreId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistories_StoreId_OrderId_OccurredAtUtc",
                table: "OrderStatusHistories",
                columns: new[] { "StoreId", "OrderId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "CheckoutRequests");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "OrderStatusHistories");

            migrationBuilder.DropTable(
                name: "Carts");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
