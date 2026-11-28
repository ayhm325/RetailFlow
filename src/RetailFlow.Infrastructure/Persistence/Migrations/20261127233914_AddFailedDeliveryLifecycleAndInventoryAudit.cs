using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFailedDeliveryLifecycleAndInventoryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('tempdb..#Aud001OrderChain') IS NOT NULL DROP TABLE #Aud001OrderChain;

;WITH OrderedHistory AS (
    SELECT
        h.StoreId,
        h.OrderId,
        o.FulfillmentMode,
        h.FromOrderStatus,
        h.ToOrderStatus,
        h.FromFulfillmentStatus,
        h.ToFulfillmentStatus,
        h.OccurredAtUtc,
        ROW_NUMBER() OVER (PARTITION BY h.StoreId, h.OrderId ORDER BY h.OccurredAtUtc ASC) AS Seq,
        COUNT(*) OVER (PARTITION BY h.StoreId, h.OrderId) AS TotalEvents,
        COUNT(*) OVER (PARTITION BY h.StoreId, h.OrderId, h.OccurredAtUtc) AS SameTsCount,
        LAG(h.ToOrderStatus) OVER (PARTITION BY h.StoreId, h.OrderId ORDER BY h.OccurredAtUtc ASC) AS PrevToOrderStatus,
        LAG(h.ToFulfillmentStatus) OVER (PARTITION BY h.StoreId, h.OrderId ORDER BY h.OccurredAtUtc ASC) AS PrevToFulfillmentStatus
    FROM dbo.OrderStatusHistories h
    INNER JOIN dbo.Orders o ON o.StoreId = h.StoreId AND o.Id = h.OrderId
),
ValidatedLinks AS (
    SELECT
        oh.*,
        CASE
            WHEN oh.SameTsCount > 1 THEN 0
            WHEN oh.Seq = 1 AND oh.FromOrderStatus IS NULL AND oh.ToOrderStatus = 1
                 AND oh.FromFulfillmentStatus IS NULL AND oh.ToFulfillmentStatus = 1 THEN 1
            WHEN oh.Seq > 1
                 AND oh.FromOrderStatus = oh.PrevToOrderStatus
                 AND oh.FromFulfillmentStatus = oh.PrevToFulfillmentStatus
                 AND (
                     (oh.FulfillmentMode IN (1, 2) AND oh.FromOrderStatus = 1 AND oh.ToOrderStatus = 2 AND oh.FromFulfillmentStatus = 1 AND oh.ToFulfillmentStatus = 1) OR
                     (oh.FulfillmentMode IN (1, 2) AND oh.FromOrderStatus = 1 AND oh.ToOrderStatus = 4 AND oh.FromFulfillmentStatus = 1 AND oh.ToFulfillmentStatus = 8) OR
                     (oh.FulfillmentMode IN (1, 2) AND oh.FromOrderStatus = 1 AND oh.ToOrderStatus = 5 AND oh.FromFulfillmentStatus = 1 AND oh.ToFulfillmentStatus = 8) OR
                     (oh.FulfillmentMode IN (1, 2) AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 5 AND oh.FromFulfillmentStatus IN (1, 2) AND oh.ToFulfillmentStatus = 8) OR
                     (oh.FulfillmentMode = 1        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 5 AND oh.FromFulfillmentStatus IN (3, 7) AND oh.ToFulfillmentStatus = 8) OR
                     (oh.FulfillmentMode = 2        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 5 AND oh.FromFulfillmentStatus = 4 AND oh.ToFulfillmentStatus = 8) OR
                     (oh.FulfillmentMode IN (1, 2) AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 2 AND oh.FromFulfillmentStatus = 1 AND oh.ToFulfillmentStatus = 2) OR
                     (oh.FulfillmentMode = 1        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 2 AND oh.FromFulfillmentStatus = 2 AND oh.ToFulfillmentStatus = 3) OR
                     (oh.FulfillmentMode = 1        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 2 AND oh.FromFulfillmentStatus = 3 AND oh.ToFulfillmentStatus = 5) OR
                     (oh.FulfillmentMode = 1        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 2 AND oh.FromFulfillmentStatus = 3 AND oh.ToFulfillmentStatus = 7) OR
                     (oh.FulfillmentMode = 2        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 2 AND oh.FromFulfillmentStatus = 2 AND oh.ToFulfillmentStatus = 4) OR
                     (oh.FulfillmentMode = 2        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 2 AND oh.FromFulfillmentStatus = 4 AND oh.ToFulfillmentStatus = 6) OR
                     (oh.FulfillmentMode = 1        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 3 AND oh.FromFulfillmentStatus = 5 AND oh.ToFulfillmentStatus = 5) OR
                     (oh.FulfillmentMode = 2        AND oh.FromOrderStatus = 2 AND oh.ToOrderStatus = 3 AND oh.FromFulfillmentStatus = 6 AND oh.ToFulfillmentStatus = 6)
                 ) THEN 1
            ELSE 0
        END AS IsLinkValid
    FROM OrderedHistory oh
)
SELECT
    StoreId,
    OrderId,
    MAX(TotalEvents) AS TotalEvents,
    MAX(CASE WHEN SameTsCount > 1 THEN 1 ELSE 0 END) AS HasTimestampTie,
    MIN(IsLinkValid) AS AllLinksValid,
    MAX(CASE WHEN Seq = TotalEvents THEN ToOrderStatus END) AS FinalToOrderStatus,
    MAX(CASE WHEN Seq = TotalEvents THEN ToFulfillmentStatus END) AS FinalToFulfillmentStatus,
    MAX(CASE WHEN ToOrderStatus = 5 AND ToFulfillmentStatus = 8 THEN FromFulfillmentStatus END) AS CancelFromFulfillmentStatus
INTO #Aud001OrderChain
FROM ValidatedLinks
GROUP BY StoreId, OrderId;

IF EXISTS (
    SELECT 1
    FROM dbo.Orders o
    LEFT JOIN #Aud001OrderChain ocs ON ocs.StoreId = o.StoreId AND ocs.OrderId = o.Id
    WHERE ocs.OrderId IS NULL
       OR ocs.TotalEvents = 0
       OR ocs.HasTimestampTie = 1
       OR ocs.AllLinksValid = 0
       OR ocs.FinalToOrderStatus <> o.OrderStatus
       OR ocs.FinalToFulfillmentStatus <> o.FulfillmentStatus
       OR (o.FulfillmentMode = 1 AND o.OrderStatus = 5 AND ocs.CancelFromFulfillmentStatus IN (3, 7))
)
OR EXISTS (
    SELECT 1
    FROM dbo.Orders o
    WHERE NOT (
        (o.OrderStatus = 1 AND o.FulfillmentMode IN (1, 2) AND o.FulfillmentStatus = 1 AND o.PaymentStatus = 1) OR
        (o.OrderStatus = 2 AND o.FulfillmentMode IN (1, 2) AND o.FulfillmentStatus IN (1, 2) AND o.PaymentStatus = 1) OR
        (o.OrderStatus = 2 AND o.FulfillmentMode = 1 AND o.FulfillmentStatus IN (3, 5, 7) AND o.PaymentStatus = 1) OR
        (o.OrderStatus = 2 AND o.FulfillmentMode = 2 AND o.FulfillmentStatus IN (4, 6) AND o.PaymentStatus = 1) OR
        (o.OrderStatus = 3 AND o.FulfillmentMode = 1 AND o.FulfillmentStatus = 5 AND o.PaymentStatus = 2) OR
        (o.OrderStatus = 3 AND o.FulfillmentMode = 2 AND o.FulfillmentStatus = 6 AND o.PaymentStatus = 2) OR
        (o.OrderStatus IN (4, 5) AND o.FulfillmentMode IN (1, 2) AND o.FulfillmentStatus = 8 AND o.PaymentStatus = 3)
    )
)
OR EXISTS (
    SELECT 1
    FROM dbo.OrderItems oi
    LEFT JOIN dbo.Products p ON p.StoreId = oi.StoreId AND p.Id = oi.ProductId
    LEFT JOIN dbo.InventoryItems ii ON ii.StoreId = oi.StoreId AND ii.ProductId = oi.ProductId
    WHERE p.Id IS NULL OR ii.Id IS NULL
)
OR EXISTS (
    SELECT 1
    FROM dbo.InventoryItems ii
    WHERE ii.Reserved < 0 OR ii.OnHand < 0 OR ii.Reserved > ii.OnHand
)
OR EXISTS (
    SELECT 1
    FROM (
        SELECT o.StoreId, oi.ProductId, SUM(oi.Quantity) AS ExpectedReserved
        FROM dbo.Orders o
        INNER JOIN dbo.OrderItems oi ON oi.StoreId = o.StoreId AND oi.OrderId = o.Id
        WHERE o.OrderStatus IN (1, 2)
        GROUP BY o.StoreId, oi.ProductId
    ) er
    FULL OUTER JOIN dbo.InventoryItems ii ON ii.StoreId = er.StoreId AND ii.ProductId = er.ProductId
    WHERE ii.Id IS NULL OR ISNULL(er.ExpectedReserved, 0) <> ii.Reserved
)
BEGIN
    DROP TABLE #Aud001OrderChain;
    THROW 51000, 'AUD-001 migration blocked: legacy history or inventory reconciliation requires manual review.', 1;
END

DROP TABLE #Aud001OrderChain;
");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_StoreId_OrderId",
                table: "OrderItems");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InventoryReservationStatus",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrderItems_StoreId_Id",
                table: "OrderItems",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrderItems_StoreId_OrderId_Id",
                table: "OrderItems",
                columns: new[] { "StoreId", "OrderId", "Id" });

            migrationBuilder.CreateTable(
                name: "FulfillmentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    FailedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    FailureReasonCode = table.Column<int>(type: "int", nullable: true),
                    FailureCategory = table.Column<int>(type: "int", nullable: true),
                    FailureNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RetryIdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RetryRequestHash = table.Column<string>(type: "char(64)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FulfillmentAttempts", x => x.Id);
                    table.UniqueConstraint("AK_FulfillmentAttempts_StoreId_OrderId_Id", x => new { x.StoreId, x.OrderId, x.Id });
                    table.CheckConstraint("CK_FulfillmentAttempts_AttemptNumber", "[AttemptNumber] IS NULL OR ([AttemptNumber] >= 1 AND [AttemptNumber] <= 3)");
                    table.CheckConstraint("CK_FulfillmentAttempts_FailureCategory", "[FailureCategory] IS NULL OR [FailureCategory] IN (1, 2, 3)");
                    table.CheckConstraint("CK_FulfillmentAttempts_IsActive_Status", "([IsActive] = 1 AND [Status] IN (1, 2)) OR ([IsActive] = 0 AND [Status] IN (3, 4, 5, 6))");
                    table.CheckConstraint("CK_FulfillmentAttempts_Status", "[Status] IN (1, 2, 3, 4, 5, 6)");
                    table.ForeignKey(
                        name: "FK_FulfillmentAttempts_Orders_StoreId_OrderId",
                        columns: x => new { x.StoreId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FulfillmentAttemptItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FulfillmentAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ReturnDisposition = table.Column<int>(type: "int", nullable: false),
                    ReturnedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FulfillmentAttemptItems", x => x.Id);
                    table.UniqueConstraint("AK_FulfillmentAttemptItems_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_FulfillmentAttemptItems_Quantity_Positive", "[Quantity] > 0");
                    table.CheckConstraint("CK_FulfillmentAttemptItems_ReturnDisposition", "[ReturnDisposition] IN (0, 1, 2, 3, 4)");
                    table.CheckConstraint("CK_FulfillmentAttemptItems_ReturnedAtUtc_Semantics", "(([ReturnDisposition] IN (2, 3) AND [ReturnedAtUtc] IS NOT NULL) OR ([ReturnDisposition] IN (0, 1, 4) AND [ReturnedAtUtc] IS NULL))");
                    table.ForeignKey(
                        name: "FK_FulfillmentAttemptItems_FulfillmentAttempts_StoreId_OrderId_FulfillmentAttemptId",
                        columns: x => new { x.StoreId, x.OrderId, x.FulfillmentAttemptId },
                        principalTable: "FulfillmentAttempts",
                        principalColumns: new[] { "StoreId", "OrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FulfillmentAttemptItems_OrderItems_StoreId_OrderId_OrderItemId",
                        columns: x => new { x.StoreId, x.OrderId, x.OrderItemId },
                        principalTable: "OrderItems",
                        principalColumns: new[] { "StoreId", "OrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FulfillmentAttemptItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OperationType = table.Column<int>(type: "int", nullable: false),
                    OnHandDelta = table.Column<int>(type: "int", nullable: false),
                    ReservedDelta = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAdjustments", x => x.Id);
                    table.CheckConstraint("CK_InventoryAdjustments_Deltas_Negative", "[OnHandDelta] < 0 AND [ReservedDelta] < 0 AND [OnHandDelta] = [ReservedDelta]");
                    table.CheckConstraint("CK_InventoryAdjustments_OperationType", "[OperationType] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_InventoryAdjustments_FulfillmentAttemptItems_StoreId_FulfillmentAttemptItemId",
                        columns: x => new { x.StoreId, x.FulfillmentAttemptItemId },
                        principalTable: "FulfillmentAttemptItems",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustments_OrderItems_StoreId_OrderId_OrderItemId",
                        columns: x => new { x.StoreId, x.OrderId, x.OrderItemId },
                        principalTable: "OrderItems",
                        principalColumns: new[] { "StoreId", "OrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustments_Orders_StoreId_OrderId",
                        columns: x => new { x.StoreId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustments_Products_StoreId_ProductId",
                        columns: x => new { x.StoreId, x.ProductId },
                        principalTable: "Products",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_InventoryReservationStatus",
                table: "OrderItems",
                sql: "[InventoryReservationStatus] IN (1, 2, 3, 4, 5)");

            migrationBuilder.CreateIndex(
                name: "IX_FulfillmentAttemptItems_StoreId_OrderId_FulfillmentAttemptId",
                table: "FulfillmentAttemptItems",
                columns: new[] { "StoreId", "OrderId", "FulfillmentAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_FulfillmentAttemptItems_StoreId_OrderId_OrderItemId",
                table: "FulfillmentAttemptItems",
                columns: new[] { "StoreId", "OrderId", "OrderItemId" });

            migrationBuilder.CreateIndex(
                name: "UX_FulfillmentAttemptItems_Attempt_OrderItem",
                table: "FulfillmentAttemptItems",
                columns: new[] { "StoreId", "FulfillmentAttemptId", "OrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FulfillmentAttempts_StoreId_OrderId_Active",
                table: "FulfillmentAttempts",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_FulfillmentAttempts_StoreId_OrderId_AttemptNumber",
                table: "FulfillmentAttempts",
                columns: new[] { "StoreId", "OrderId", "AttemptNumber" },
                unique: true,
                filter: "[AttemptNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_FulfillmentAttempts_StoreId_OrderId_RetryIdempotencyKey",
                table: "FulfillmentAttempts",
                columns: new[] { "StoreId", "OrderId", "RetryIdempotencyKey" },
                unique: true,
                filter: "[RetryIdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_StoreId_FulfillmentAttemptItemId",
                table: "InventoryAdjustments",
                columns: new[] { "StoreId", "FulfillmentAttemptItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_StoreId_OrderId_OrderItemId",
                table: "InventoryAdjustments",
                columns: new[] { "StoreId", "OrderId", "OrderItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_StoreId_ProductId",
                table: "InventoryAdjustments",
                columns: new[] { "StoreId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryAdjustments_OrderItem_WriteOff",
                table: "InventoryAdjustments",
                columns: new[] { "StoreId", "OrderItemId" },
                unique: true,
                filter: "[OperationType] IN (1, 2)");

            migrationBuilder.Sql(@"
UPDATE oi
SET oi.InventoryReservationStatus =
    CASE
        WHEN o.OrderStatus = 3 THEN 4 -- Committed
        WHEN o.OrderStatus IN (4, 5) THEN 3 -- Released
        WHEN o.OrderStatus = 2 AND o.FulfillmentStatus = 7 THEN 2 -- ReturnPending
        ELSE 1 -- Active
    END
FROM dbo.OrderItems oi
INNER JOIN dbo.Orders o ON o.StoreId = oi.StoreId AND o.Id = oi.OrderId;

;WITH HistoryTimestamps AS (
    SELECT
        h.StoreId,
        h.OrderId,
        MAX(CASE WHEN h.ToFulfillmentStatus = 2 THEN h.OccurredAtUtc END) AS PreparedAtUtc,
        MAX(CASE WHEN h.ToFulfillmentStatus = 3 THEN h.OccurredAtUtc END) AS DispatchedAtUtc,
        MAX(CASE WHEN h.ToFulfillmentStatus = 5 AND h.ToOrderStatus = 2 THEN h.OccurredAtUtc END) AS DeliveredAtUtc,
        MAX(CASE WHEN h.ToFulfillmentStatus = 7 THEN h.OccurredAtUtc END) AS FailedAtUtc,
        MAX(CASE WHEN h.ToOrderStatus = 5 AND h.ToFulfillmentStatus = 8 THEN h.FromFulfillmentStatus END) AS CancelFromFulfillmentStatus
    FROM dbo.OrderStatusHistories h
    GROUP BY h.StoreId, h.OrderId
)
INSERT INTO dbo.FulfillmentAttempts (
    Id, StoreId, OrderId, AttemptNumber, Status, IsActive,
    PreparedAtUtc, DispatchedAtUtc, DeliveredAtUtc, FailedAtUtc, CancelledAtUtc,
    FailureReasonCode, FailureCategory, FailureNote,
    ResolvedAtUtc, ResolvedByUserId, ResolutionNote, RetryIdempotencyKey, RetryRequestHash
)
SELECT
    NEWID(),
    o.StoreId,
    o.Id,
    CASE
        WHEN o.FulfillmentStatus IN (3, 5, 7) THEN 1
        ELSE NULL
    END,
    CASE
        WHEN o.FulfillmentStatus = 2 THEN 1 -- Prepared
        WHEN o.FulfillmentStatus = 3 THEN 2 -- Dispatched
        WHEN o.FulfillmentStatus = 5 THEN 3 -- Delivered
        WHEN o.FulfillmentStatus = 7 THEN 4 -- Failed
        WHEN o.OrderStatus = 5 AND ht.CancelFromFulfillmentStatus = 2 THEN 5 -- Abandoned
    END,
    CASE
        WHEN o.FulfillmentStatus IN (2, 3) THEN CAST(1 AS bit)
        ELSE CAST(0 AS bit)
    END,
    ht.PreparedAtUtc,
    ht.DispatchedAtUtc,
    ht.DeliveredAtUtc,
    ht.FailedAtUtc,
    NULL,
    CASE WHEN o.FulfillmentStatus = 7 THEN 99 ELSE NULL END, -- LegacyUnknown
    CASE WHEN o.FulfillmentStatus = 7 THEN 2 ELSE NULL END,  -- RequiresCorrection
    CASE WHEN o.FulfillmentStatus = 7 THEN N'Legacy failed delivery backfilled; requires merchant review.' ELSE NULL END,
    NULL, NULL, NULL, NULL, NULL
FROM dbo.Orders o
INNER JOIN HistoryTimestamps ht ON ht.StoreId = o.StoreId AND ht.OrderId = o.Id
WHERE o.FulfillmentMode = 1
  AND (
      o.FulfillmentStatus IN (2, 3, 5, 7)
      OR (o.OrderStatus = 5 AND ht.CancelFromFulfillmentStatus = 2)
  );

INSERT INTO dbo.FulfillmentAttemptItems (
    Id, StoreId, OrderId, FulfillmentAttemptId, OrderItemId, ProductId, Quantity,
    ReturnDisposition, ReturnedAtUtc, ResolvedAtUtc, ResolvedByUserId, ResolutionNote
)
SELECT
    NEWID(),
    fa.StoreId,
    fa.OrderId,
    fa.Id,
    oi.Id,
    oi.ProductId,
    oi.Quantity,
    CASE WHEN fa.Status = 4 THEN 1 ELSE 0 END, -- Pending for Failed, NotRequired otherwise
    NULL, NULL, NULL, NULL
FROM dbo.FulfillmentAttempts fa
INNER JOIN dbo.OrderItems oi ON oi.StoreId = fa.StoreId AND oi.OrderId = fa.OrderId;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAdjustments");

            migrationBuilder.DropTable(
                name: "FulfillmentAttemptItems");

            migrationBuilder.DropTable(
                name: "FulfillmentAttempts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrderItems_StoreId_Id",
                table: "OrderItems");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrderItems_StoreId_OrderId_Id",
                table: "OrderItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_InventoryReservationStatus",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InventoryReservationStatus",
                table: "OrderItems");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_StoreId_OrderId",
                table: "OrderItems",
                columns: new[] { "StoreId", "OrderId" });
        }
    }
}
