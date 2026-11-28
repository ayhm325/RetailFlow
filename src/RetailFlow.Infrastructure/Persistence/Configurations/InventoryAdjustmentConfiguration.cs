using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.ToTable("InventoryAdjustments", t =>
        {
            t.HasCheckConstraint("CK_InventoryAdjustments_OperationType", "[OperationType] IN (1, 2)");
            t.HasCheckConstraint("CK_InventoryAdjustments_Deltas_Negative", "[OnHandDelta] < 0 AND [ReservedDelta] < 0 AND [OnHandDelta] = [ReservedDelta]");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.OrderItemId).IsRequired();
        builder.Property(x => x.FulfillmentAttemptItemId);
        builder.Property(x => x.OperationType).HasConversion<int>().IsRequired();
        builder.Property(x => x.OnHandDelta).IsRequired();
        builder.Property(x => x.ReservedDelta).IsRequired();
        builder.Property(x => x.ReasonCode).HasConversion<int?>();
        builder.Property(x => x.ActorUserId).IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasIndex(x => new { x.StoreId, x.OrderItemId })
            .IsUnique()
            .HasDatabaseName("UX_InventoryAdjustments_OrderItem_WriteOff")
            .HasFilter("[OperationType] IN (1, 2)");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.ProductId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.OrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrderItem>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.OrderId, x.OrderItemId })
            .HasPrincipalKey(x => new { x.StoreId, x.OrderId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FulfillmentAttemptItem>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.FulfillmentAttemptItemId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
