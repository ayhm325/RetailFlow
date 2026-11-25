using Microsoft.EntityFrameworkCore;

using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence;

public sealed class RetailFlowDbContext : DbContext
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(RetailFlowDbContext).Assembly);
    }
}
