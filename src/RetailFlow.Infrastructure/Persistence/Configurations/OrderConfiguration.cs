using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", t =>
        {
            t.HasCheckConstraint("CK_Orders_OrderStatus", "[OrderStatus] IN (1, 2, 3, 4, 5)");
            t.HasCheckConstraint("CK_Orders_FulfillmentMode", "[FulfillmentMode] IN (1, 2)");
            t.HasCheckConstraint("CK_Orders_FulfillmentStatus", "[FulfillmentStatus] IN (1, 2, 3, 4, 5, 6, 7, 8)");
            t.HasCheckConstraint("CK_Orders_PaymentStatus", "[PaymentStatus] IN (1, 2, 3)");
            t.HasCheckConstraint("CK_Orders_Totals_NonNegative", "[Subtotal] >= 0 AND [DeliveryFee] >= 0 AND [Total] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.StoreId, x.Id }).HasName("AK_Orders_StoreId_Id");

        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.OrderNumber).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FulfillmentMode).HasConversion<int>().IsRequired();
        builder.Property(x => x.OrderStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.FulfillmentStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.PaymentStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.CurrencyCode).HasColumnType("char(3)").IsRequired();
        builder.Property(x => x.Subtotal).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.DeliveryFee).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Total).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.DeliveryZoneId);
        builder.Property(x => x.DeliveryZoneName).HasMaxLength(120);
        builder.Property(x => x.DeliveryAddress).HasMaxLength(500);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.ReservationExpiresAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.StoreId, x.OrderNumber })
            .IsUnique()
            .HasDatabaseName("UX_Orders_StoreId_OrderNumber");

        builder.HasIndex(x => new { x.StoreId, x.CustomerId, x.CreatedAtUtc })
            .HasDatabaseName("IX_Orders_StoreId_CustomerId_CreatedAtUtc");

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(x => x.CurrencyCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => new { x.StoreId, x.OrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.History)
            .WithOne()
            .HasForeignKey(x => new { x.StoreId, x.OrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", t =>
        {
            t.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderItems_Prices_NonNegative", "[UnitPrice] >= 0 AND [LineTotal] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Sku).HasMaxLength(64);
        builder.Property(x => x.UnitPrice).HasPrecision(19, 4).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.LineTotal).HasPrecision(19, 4).IsRequired();
    }
}

internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.ActorUserId);
        builder.Property(x => x.ActorRole).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FromOrderStatus).HasConversion<int?>();
        builder.Property(x => x.ToOrderStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.FromFulfillmentStatus).HasConversion<int?>();
        builder.Property(x => x.ToFulfillmentStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();

        builder.HasIndex(x => new { x.StoreId, x.OrderId, x.OccurredAtUtc })
            .HasDatabaseName("IX_OrderStatusHistories_StoreId_OrderId_OccurredAtUtc");
    }
}
