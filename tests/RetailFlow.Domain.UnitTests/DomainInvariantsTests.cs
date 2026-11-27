using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Fulfillment;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Domain.UnitTests;

public sealed class DomainInvariantsTests
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private static readonly Currency Usd = Currency.Create("USD", "840", 2);

    [Fact]
    public void Store_LifecycleTransitions_EnforceValidStateMachine()
    {
        var store = Store.Create(
            Guid.NewGuid(),
            "Amman Roastery",
            "Amman-Roastery",
            StoreType.Physical,
            StoreStatus.PendingApproval,
            DateTimeOffset.UtcNow);

        Assert.Equal("amman-roastery", store.Slug);
        Assert.False(store.IsPubliclyAccessible);

        store.Approve();
        Assert.Equal(StoreStatus.Active, store.Status);
        Assert.True(store.IsPubliclyAccessible);

        store.Suspend();
        Assert.Equal(StoreStatus.Suspended, store.Status);
        Assert.False(store.IsPubliclyAccessible);

        store.Approve();
        Assert.Equal(StoreStatus.Active, store.Status);

        Assert.Throws<InvalidOperationException>(() => store.Reject());
    }

    [Fact]
    public void Store_UpdateBranding_NormalizesAndValidatesName()
    {
        var store = Store.Create(
            Guid.NewGuid(),
            "Old Name",
            "old-name",
            StoreType.OnlineOnly,
            StoreStatus.Active,
            DateTimeOffset.UtcNow);

        store.UpdateBranding(
            "  New Roastery  ",
            " https://cdn.example/logo.png ",
            " #1E293B ",
            " hello@example.com ",
            " +962790000000 ");

        Assert.Equal("New Roastery", store.Name);
        Assert.Equal("https://cdn.example/logo.png", store.LogoUrl);
        Assert.Equal("#1E293B", store.PrimaryColor);
        Assert.Equal("hello@example.com", store.ContactEmail);
        Assert.Equal("+962790000000", store.ContactPhone);

        Assert.Throws<ArgumentException>(() => store.UpdateBranding("   "));
    }

    [Fact]
    public void StoreSettings_OnlineOnly_RejectsPublicAddressAndPickup()
    {
        var onlineStore = Store.Create(
            Guid.NewGuid(),
            "Cloud Shop",
            "cloud-shop",
            StoreType.OnlineOnly,
            StoreStatus.Active,
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => StoreSettings.Create(
            Guid.NewGuid(),
            onlineStore,
            Jod,
            "Asia/Amman",
            deliveryEnabled: true,
            pickupEnabled: false,
            publicAddress: "123 Main St",
            fulfillmentAddress: "Warehouse 4"));

        var settings = StoreSettings.Create(
            Guid.NewGuid(),
            onlineStore,
            Jod,
            "Asia/Amman",
            deliveryEnabled: true,
            pickupEnabled: false,
            publicAddress: null,
            fulfillmentAddress: "Warehouse 4");

        Assert.Null(settings.PublicAddress);
        Assert.Equal("Warehouse 4", settings.FulfillmentAddress);

        Assert.Throws<InvalidOperationException>(() =>
            settings.UpdateAddresses(onlineStore, "123 Main St", "Warehouse 5"));
    }

    [Fact]
    public void Product_UpdateDetails_AndLifecycle_EnforceCurrencyPrecisionAndTransitions()
    {
        var store = Store.Create(
            Guid.NewGuid(),
            "Roastery",
            "roastery",
            StoreType.Physical,
            StoreStatus.Active,
            DateTimeOffset.UtcNow);
        var settings = StoreSettings.Create(
            Guid.NewGuid(),
            store,
            Jod,
            "Asia/Amman",
            deliveryEnabled: true,
            pickupEnabled: true);

        var product = Product.Create(
            Guid.NewGuid(),
            store.Id,
            Guid.NewGuid(),
            "Espresso",
            12.345m,
            settings,
            Jod,
            ProductStatus.Draft,
            DateTimeOffset.UtcNow,
            "Dark roast",
            "ESP-01");

        product.Publish();
        Assert.Equal(ProductStatus.Published, product.Status);

        product.UpdateDetails(
            product.CategoryId,
            "Espresso Reserve",
            15.500m,
            settings,
            Jod,
            "Single origin",
            "ESP-02");

        Assert.Equal("Espresso Reserve", product.Name);
        Assert.Equal(15.500m, product.Price);
        Assert.Equal("ESP-02", product.Sku);

        Assert.Throws<ArgumentException>(() =>
            product.UpdateDetails(product.CategoryId, "Bad Currency", 10.00m, settings, Usd));
        Assert.Throws<ArgumentException>(() =>
            product.UpdateDetails(product.CategoryId, "Bad Precision", 10.1234m, settings, Jod));

        product.Archive();
        Assert.Equal(ProductStatus.Archived, product.Status);

        product.MoveToDraft();
        Assert.Equal(ProductStatus.Draft, product.Status);
    }

    [Fact]
    public void InventoryItem_SetAndAdjustOnHand_EnforceReservedInvariant()
    {
        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            onHand: 10,
            reserved: 4);

        Assert.Equal(6, item.Available);

        item.SetOnHand(8);
        Assert.Equal(4, item.Available);

        item.AdjustOnHand(-4);
        Assert.Equal(4, item.OnHand);
        Assert.Equal(0, item.Available);

        Assert.Throws<InventoryOnHandBelowReservedException>(() => item.SetOnHand(3));
        Assert.Throws<InventoryOnHandBelowReservedException>(() => item.AdjustOnHand(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.SetOnHand(-1));
    }

    [Fact]
    public void DeliveryZone_EnforcesStoreCurrencyPrecisionAndNonNegativeFee()
    {
        var store = Store.Create(
            Guid.NewGuid(),
            "Roastery",
            "roastery",
            StoreType.Physical,
            StoreStatus.Active,
            DateTimeOffset.UtcNow);
        var settings = StoreSettings.Create(
            Guid.NewGuid(),
            store,
            Jod,
            "Asia/Amman",
            deliveryEnabled: true,
            pickupEnabled: true);

        var zone = DeliveryZone.Create(
            Guid.NewGuid(),
            store.Id,
            "  West Amman ",
            2.500m,
            settings,
            Jod,
            DateTimeOffset.UtcNow);

        Assert.Equal("West Amman", zone.Name);
        Assert.Equal("WEST AMMAN", zone.NormalizedName);
        Assert.Equal(2.500m, zone.FlatFee);
        Assert.True(zone.IsEnabled);

        zone.SetEnabled(false);
        Assert.False(zone.IsEnabled);

        zone.UpdateFee(3.250m, settings, Jod);
        Assert.Equal(3.250m, zone.FlatFee);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            zone.UpdateFee(-1.000m, settings, Jod));
        Assert.Throws<ArgumentException>(() =>
            zone.UpdateFee(1.2345m, settings, Jod));
    }

    [Fact]
    public void Store_InvalidRepeatedOrForbiddenLifecycleTransitions_Throw()
    {
        var pending = Store.Create(Guid.NewGuid(), "Pending", "pending", StoreType.OnlineOnly, StoreStatus.PendingApproval, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => pending.Suspend());

        pending.Reject();
        Assert.Equal(StoreStatus.Rejected, pending.Status);
        Assert.Throws<InvalidOperationException>(() => pending.Approve());
        Assert.Throws<InvalidOperationException>(() => pending.Reject());

        var active = Store.Create(Guid.NewGuid(), "Active", "active", StoreType.OnlineOnly, StoreStatus.PendingApproval, DateTimeOffset.UtcNow);
        active.Approve();
        Assert.Throws<InvalidOperationException>(() => active.Approve());
    }

    [Fact]
    public void ProductImage_EnforcesHttpHttpsUrlLengthAltTextAndNonNegativeSortOrder()
    {
        var storeId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var valid = ProductImage.Create(Guid.NewGuid(), storeId, productId, " https://cdn.example/img.jpg ", 0, "  Front view  ");
        Assert.Equal("https://cdn.example/img.jpg", valid.ImageUrl);
        Assert.Equal("Front view", valid.AltText);

        Assert.Throws<ArgumentException>(() => ProductImage.Create(Guid.NewGuid(), storeId, productId, "javascript:alert(1)"));
        Assert.Throws<ArgumentException>(() => ProductImage.Create(Guid.NewGuid(), storeId, productId, "/relative/path.png"));
        Assert.Throws<ArgumentException>(() => ProductImage.Create(Guid.NewGuid(), storeId, productId, "https://cdn.example/" + new string('a', 1000)));
        Assert.Throws<ArgumentException>(() => ProductImage.Create(Guid.NewGuid(), storeId, productId, "https://cdn.example/a.png", 0, new string('b', 251)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductImage.Create(Guid.NewGuid(), storeId, productId, "https://cdn.example/a.png", -1));
    }
}
