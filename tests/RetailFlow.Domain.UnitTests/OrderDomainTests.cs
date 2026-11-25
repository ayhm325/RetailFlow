using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Domain.UnitTests;

public sealed class OrderDomainTests
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    [Fact]
    public void Order_DeliveryHappyPath_EnforcesAdr0005TransitionsAndCodCompletion()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "Roastery", "roastery", StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", deliveryEnabled: true, pickupEnabled: true);
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var items = new[]
        {
            OrderItem.Create(Guid.NewGuid(), store.Id, orderId, Guid.NewGuid(), "Beans", "BN-1", 10.500m, 2, Jod)
        };

        var order = Order.Create(
            orderId,
            store,
            settings,
            Jod,
            customerId,
            "ORD-1001",
            FulfillmentMode.Delivery,
            items,
            2.500m,
            Guid.NewGuid(),
            "West Amman",
            "Rainbow St 10",
            now);

        Assert.Equal(21.000m, order.Subtotal);
        Assert.Equal(23.500m, order.Total);
        Assert.Equal(now.AddMinutes(15), order.ReservationExpiresAtUtc); // OD-001

        // Cannot advance fulfillment or collect COD before Confirm
        Assert.Throws<InvalidOperationException>(() =>
            order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() =>
            order.CollectCodPayment(merchantId, "MerchantOwner", now.AddMinutes(1)));

        order.Confirm(merchantId, "MerchantOwner", now.AddMinutes(1));

        // OD-002: Customer cannot self-cancel after Pending
        Assert.Throws<InvalidOperationException>(() =>
            order.CancelByCustomer(customerId, "Changed mind", now.AddMinutes(2)));

        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Preparing, now.AddMinutes(2));
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.OutForDelivery, now.AddMinutes(3));
        order.AdvanceFulfillment(merchantId, "MerchantOwner", FulfillmentStatus.Delivered, now.AddMinutes(4));
        order.CollectCodPayment(merchantId, "MerchantOwner", now.AddMinutes(5));

        Assert.Equal(OrderStatus.Completed, order.OrderStatus);
        Assert.Equal(FulfillmentStatus.Delivered, order.FulfillmentStatus);
        Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
        Assert.Equal(6, order.History.Count);
    }

    [Fact]
    public void Order_CustomerSelfCancellation_AllowedOnlyInPending_PerOd002()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "Roastery", "roastery", StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", deliveryEnabled: true, pickupEnabled: true);
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var order = Order.Create(
            orderId,
            store,
            settings,
            Jod,
            customerId,
            "ORD-1002",
            FulfillmentMode.Pickup,
            [OrderItem.Create(Guid.NewGuid(), store.Id, orderId, Guid.NewGuid(), "Espresso", null, 3.000m, 1, Jod)],
            0m,
            null,
            null,
            null,
            now);

        Assert.Throws<InvalidOperationException>(() =>
            order.CancelByCustomer(Guid.NewGuid(), "Wrong actor", now.AddMinutes(1)));

        order.CancelByCustomer(customerId, "No longer needed", now.AddMinutes(1));

        Assert.Equal(OrderStatus.Cancelled, order.OrderStatus);
        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
        Assert.Equal(PaymentStatus.Voided, order.PaymentStatus);
    }

    [Fact]
    public void InventoryItem_ReserveReleaseCommit_EnforcesAvailabilityAndBounds()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), onHand: 5, reserved: 1);

        item.Reserve(3);
        Assert.Equal(4, item.Reserved);
        Assert.Equal(1, item.Available);

        Assert.Throws<InvalidOperationException>(() => item.Reserve(2));

        item.ReleaseReservation(2);
        Assert.Equal(2, item.Reserved);
        Assert.Equal(3, item.Available);

        item.CommitReservation(2);
        Assert.Equal(0, item.Reserved);
        Assert.Equal(3, item.OnHand);
    }
}
