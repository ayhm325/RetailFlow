using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Fulfillment;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Infrastructure.Persistence;

public sealed class RetailFlowDbContext
    : IdentityDbContext<RetailFlowIdentityUser, RetailFlowIdentityRole, Guid>,
      IDataProtectionKeyContext
{
    public RetailFlowDbContext(DbContextOptions<RetailFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();

    public DbSet<CheckoutRequest> CheckoutRequests => Set<CheckoutRequest>();

    public DbSet<SessionTicket> SessionTickets => Set<SessionTicket>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(RetailFlowDbContext).Assembly);
    }
}
