using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Domain.UnitTests;

public sealed class FailedDeliveryLifecycleDomainTests
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    private static (Order Order, InventoryItem InvA, InventoryItem InvB) CreateConfirmedDeliveryOrder(
        FulfillmentMode mode = FulfillmentMode.Delivery,
        int qtyA = 2,
        int qtyB = 3,
        int onHandA = 10,
        int onHandB = 10)
    {
        var now = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
        var store = Store.Create(Guid.NewGuid(), "Roastery", "roastery", StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", deliveryEnabled: true, pickupEnabled: true);
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var prodA = Guid.NewGuid();
        var prodB = Guid.NewGuid();

        var invA = InventoryItem.Create(Guid.NewGuid(), store.Id, prodA, onHandA, 0);
        var invB = InventoryItem.Create(Guid.NewGuid(), store.Id, prodB, onHandB, 0);
        invA.Reserve(qtyA);
        invB.Reserve(qtyB);

        var items = new[]
        {
            OrderItem.Create(Guid.NewGuid(), store.Id, orderId, prodA, "Beans A", "A-1", 10.000m, qtyA, Jod),
            OrderItem.Create(Guid.NewGuid(), store.Id, orderId, prodB, "Beans B", "B-1", 5.000m, qtyB, Jod)
        };

        var order = Order.Create(
            orderId,
            store,
            settings,
            Jod,
            customerId,
            $"ORD-{orderId.ToString("N")[..6]}",
            mode,
            items,
            mode == FulfillmentMode.Delivery ? 2.000m : 0m,
            mode == FulfillmentMode.Delivery ? Guid.NewGuid() : null,
            mode == FulfillmentMode.Delivery ? "Amman" : null,
            mode == FulfillmentMode.Delivery ? "St 1" : null,
            now);

        order.Confirm(merchantId, "MerchantOwner", now.AddMinutes(1));
        return (order, invA, invB);
    }

    [Fact]
    public void InitialDeliveryPreparing_CreatesOnePreparedAttempt_AndDuplicatePreparingThrows()
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder();
        var t1 = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t1);

        Assert.Single(order.FulfillmentAttempts);
        var attempt = order.FulfillmentAttempts.Single();
        Assert.Equal(FulfillmentAttemptStatus.Prepared, attempt.Status);
        Assert.Null(attempt.AttemptNumber);
        Assert.True(attempt.IsActive);
        Assert.Equal(2, attempt.Items.Count);
        Assert.All(attempt.Items, i =>
        {
            Assert.Equal(ReturnDisposition.NotRequired, i.ReturnDisposition);
            Assert.Null(i.ReturnedAtUtc);
        });

        Assert.Throws<OrderStateConflictException>(() =>
            order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t1.AddMinutes(1)));
        Assert.Single(order.FulfillmentAttempts);
    }

    [Fact]
    public void PickupWorkflow_CreatesZeroDeliveryAttempts()
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder(FulfillmentMode.Pickup);
        var t1 = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t1);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.ReadyForPickup, t1.AddMinutes(1));
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.PickedUp, t1.AddMinutes(2));

        Assert.Empty(order.FulfillmentAttempts);
    }

    [Fact]
    public void DispatchAndRetry_AssignAttemptNumbers1To3_AndBlockFourthDispatch()
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder();
        var t = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();

        // Attempt 1
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(1));
        var a1 = order.FulfillmentAttempts.Single();
        Assert.Equal(FulfillmentAttemptStatus.Dispatched, a1.Status);
        Assert.Equal(1, a1.AttemptNumber);
        // Prepared attempt cannot dispatch twice
        Assert.Throws<OrderStateConflictException>(() =>
            order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(2)));

        order.RecordDeliveryFailure(merchantId, "MerchantOwner", DeliveryFailureReasonCode.CustomerUnavailable, "No one home", t.AddMinutes(3));
        order.ResolveInventoryDisposition(
            merchantId,
            "MerchantOwner",
            a1.Id,
            order.Items.Select(i => (i.Id, ReturnDisposition.Restockable, (string?)"Returned")).ToList(),
            t.AddMinutes(4));

        // Attempt 2
        var a2 = order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(5), "retry-2", "hash-2");
        Assert.Equal(FulfillmentAttemptStatus.Prepared, a2.Status);
        Assert.Null(a2.AttemptNumber);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(6));
        Assert.Equal(2, a2.AttemptNumber);

        order.RecordDeliveryFailure(merchantId, "MerchantOwner", DeliveryFailureReasonCode.NoAnswer, "Phone off", t.AddMinutes(7));
        order.ResolveInventoryDisposition(
            merchantId,
            "MerchantOwner",
            a2.Id,
            order.Items.Select(i => (i.Id, ReturnDisposition.Restockable, (string?)"Returned")).ToList(),
            t.AddMinutes(8));

        // Attempt 3
        var a3 = order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(9), "retry-3", "hash-3");
        Assert.Null(a3.AttemptNumber);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(10));
        Assert.Equal(3, a3.AttemptNumber);

        order.RecordDeliveryFailure(merchantId, "MerchantOwner", DeliveryFailureReasonCode.RescheduleRequested, "Later", t.AddMinutes(11));
        order.ResolveInventoryDisposition(
            merchantId,
            "MerchantOwner",
            a3.Id,
            order.Items.Select(i => (i.Id, ReturnDisposition.Restockable, (string?)"Returned")).ToList(),
            t.AddMinutes(12));

        // 4th attempt blocked
        Assert.Throws<OrderStateConflictException>(() =>
            order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(13), "retry-4", "hash-4"));
    }

    [Fact]
    public void CancelPrepared_AbandonsAttemptAndConsumesZeroPhysicalAttempts()
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder();
        var t = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t);
        order.CancelByMerchantOrTimeout(merchantId, "MerchantOwner", "Merchant cancelled", t.AddMinutes(1));

        var attempt = order.FulfillmentAttempts.Single();
        Assert.Equal(FulfillmentAttemptStatus.Abandoned, attempt.Status);
        Assert.False(attempt.IsActive);
        Assert.Null(attempt.AttemptNumber);
        Assert.All(order.Items, i => Assert.Equal(InventoryReservationStatus.Released, i.ReservationStatus));
        Assert.Equal(OrderReservationSummary.Settled, order.ReservationSummary);
    }

    [Fact]
    public void CancelDispatched_MarksAttemptCancelled_AndSetsItemsPendingReturn()
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder();
        var t = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(1));
        order.CancelByMerchantOrTimeout(merchantId, "MerchantOwner", "Cancelled in flight", t.AddMinutes(2));

        var attempt = order.FulfillmentAttempts.Single();
        Assert.Equal(FulfillmentAttemptStatus.Cancelled, attempt.Status);
        Assert.False(attempt.IsActive);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.All(attempt.Items, i => Assert.Equal(ReturnDisposition.Pending, i.ReturnDisposition));
        Assert.All(order.Items, i => Assert.Equal(InventoryReservationStatus.ReturnPending, i.ReservationStatus));
        Assert.Equal(OrderReservationSummary.ReturnPending, order.ReservationSummary);
    }

    [Fact]
    public void FailedAttempt_RemainsFailedAfterLaterOrderCancellation()
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder();
        var t = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(1));
        order.RecordDeliveryFailure(merchantId, "MerchantOwner", DeliveryFailureReasonCode.NoAnswer, null, t.AddMinutes(2));
        order.CancelByMerchantOrTimeout(merchantId, "MerchantOwner", "Cancel after fail", t.AddMinutes(3));

        var attempt = order.FulfillmentAttempts.Single();
        Assert.Equal(FulfillmentAttemptStatus.Failed, attempt.Status);
        Assert.Equal(OrderStatus.Cancelled, order.OrderStatus);
    }

    [Theory]
    [InlineData(DeliveryFailureReasonCode.CustomerUnavailable, DeliveryFailureCategory.Temporary, false)]
    [InlineData(DeliveryFailureReasonCode.IncompleteAddress, DeliveryFailureCategory.RequiresCorrection, false)]
    [InlineData(DeliveryFailureReasonCode.CustomerRefused, DeliveryFailureCategory.NonRecoverable, true)]
    [InlineData(DeliveryFailureReasonCode.LegacyUnknown, DeliveryFailureCategory.RequiresCorrection, false)]
    [InlineData((DeliveryFailureReasonCode)999, DeliveryFailureCategory.NonRecoverable, true)]
    public void FailureClassification_EnforcesCategoryAndRetryGuards(
        DeliveryFailureReasonCode reasonCode,
        DeliveryFailureCategory expectedCategory,
        bool shouldBlockRetryEvenAfterReturn)
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder();
        var t = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(1));
        order.RecordDeliveryFailure(merchantId, "MerchantOwner", reasonCode, "Note", t.AddMinutes(2));

        var attempt = order.FulfillmentAttempts.Single();
        Assert.Equal(expectedCategory, attempt.FailureCategory);

        // Retry blocked while ReturnPending
        Assert.Throws<OrderStateConflictException>(() =>
            order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(3)));

        order.ResolveInventoryDisposition(
            merchantId,
            "MerchantOwner",
            attempt.Id,
            order.Items.Select(i => (i.Id, ReturnDisposition.Restockable, (string?)"Returned")).ToList(),
            t.AddMinutes(4));

        if (expectedCategory == DeliveryFailureCategory.RequiresCorrection)
        {
            // Blocked until correction is recorded
            Assert.Throws<OrderStateConflictException>(() =>
                order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(5)));

            order.ResolveDeliveryCorrection(merchantId, "MerchantOwner", "Fixed address", t.AddMinutes(6));
            var retry = order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(7));
            Assert.Equal(FulfillmentAttemptStatus.Prepared, retry.Status);
        }
        else if (shouldBlockRetryEvenAfterReturn)
        {
            Assert.Throws<OrderStateConflictException>(() =>
                order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(5)));
        }
        else
        {
            var retry = order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(5));
            Assert.Equal(FulfillmentAttemptStatus.Prepared, retry.Status);
        }
    }

    [Fact]
    public void MixedOrderItems_RestockableAndDamaged_AndLost_UpdateIndependently()
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder();
        var t = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();
        var itemA = order.Items.ElementAt(0);
        var itemB = order.Items.ElementAt(1);

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(1));
        order.RecordDeliveryFailure(merchantId, "MerchantOwner", DeliveryFailureReasonCode.CustomerUnavailable, null, t.AddMinutes(2));
        var attempt = order.FulfillmentAttempts.Single();

        // Confirmed Order: Item A Restockable (Active), Item B Damaged (ReturnPending) -> PartiallyResolved, Retry blocked
        order.ResolveInventoryDisposition(
            merchantId,
            "MerchantOwner",
            attempt.Id,
            [(itemA.Id, ReturnDisposition.Restockable, "Good"), (itemB.Id, ReturnDisposition.Damaged, "Crushed")],
            t.AddMinutes(3));

        Assert.Equal(InventoryReservationStatus.Active, itemA.ReservationStatus);
        Assert.Equal(InventoryReservationStatus.ReturnPending, itemB.ReservationStatus);
        Assert.Equal(OrderReservationSummary.PartiallyResolved, order.ReservationSummary);
        Assert.NotNull(attempt.Items.Single(i => i.OrderItemId == itemB.Id).ReturnedAtUtc);
        Assert.Throws<OrderStateConflictException>(() =>
            order.RetryDelivery(merchantId, "MerchantOwner", t.AddMinutes(4)));

        // Cancel Confirmed Order -> Item A (already Restockable) becomes Released, Item B (Damaged) becomes WrittenOff
        order.CancelByMerchantOrTimeout(merchantId, "MerchantOwner", "Cancel damaged order", t.AddMinutes(5));
        Assert.Equal(InventoryReservationStatus.Released, itemA.ReservationStatus);
        Assert.Equal(InventoryReservationStatus.WrittenOff, itemB.ReservationStatus);
        Assert.Equal(OrderReservationSummary.Settled, order.ReservationSummary);

        // Repeated matching resolution is idempotent; conflicting resolution throws
        order.ResolveInventoryDisposition(
            merchantId,
            "MerchantOwner",
            attempt.Id,
            [(itemA.Id, ReturnDisposition.Restockable, "Repeat"), (itemB.Id, ReturnDisposition.Damaged, "Repeat")],
            t.AddMinutes(6));

        Assert.Throws<OrderStateConflictException>(() =>
            order.ResolveInventoryDisposition(
                merchantId,
                "MerchantOwner",
                attempt.Id,
                [(itemA.Id, ReturnDisposition.Lost, "Conflict")],
                t.AddMinutes(7)));
    }

    [Fact]
    public void LostDisposition_OnCancelledOrder_HasNullReturnedAtUtc_AndBecomesWrittenOff()
    {
        var (order, _, _) = CreateConfirmedDeliveryOrder();
        var t = order.CreatedAtUtc.AddMinutes(2);
        var merchantId = Guid.NewGuid();
        var itemA = order.Items.ElementAt(0);
        var itemB = order.Items.ElementAt(1);

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, t);
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, t.AddMinutes(1));
        order.CancelByMerchantOrTimeout(merchantId, "MerchantOwner", "Cancel in flight", t.AddMinutes(2));
        var attempt = order.FulfillmentAttempts.Single();

        order.ResolveInventoryDisposition(
            merchantId,
            "MerchantOwner",
            attempt.Id,
            [(itemA.Id, ReturnDisposition.Restockable, "OK"), (itemB.Id, ReturnDisposition.Lost, "Lost by courier")],
            t.AddMinutes(3));

        var attemptItemA = attempt.Items.Single(i => i.OrderItemId == itemA.Id);
        var attemptItemB = attempt.Items.Single(i => i.OrderItemId == itemB.Id);
        Assert.NotNull(attemptItemA.ReturnedAtUtc);
        Assert.Null(attemptItemB.ReturnedAtUtc);
        Assert.Equal(InventoryReservationStatus.Released, itemA.ReservationStatus);
        Assert.Equal(InventoryReservationStatus.WrittenOff, itemB.ReservationStatus);
    }

    [Fact]
    public void InventoryItem_WriteOffReservation_EnforcesInvariantsAndDecrementsBoth()
    {
        var inv = InventoryItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), onHand: 10, reserved: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => inv.WriteOffReservation(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => inv.WriteOffReservation(-1));
        Assert.Throws<InvalidOperationException>(() => inv.WriteOffReservation(5));

        inv.WriteOffReservation(3);
        Assert.Equal(7, inv.OnHand);
        Assert.Equal(1, inv.Reserved);
        Assert.Equal(6, inv.Available);
    }
}
