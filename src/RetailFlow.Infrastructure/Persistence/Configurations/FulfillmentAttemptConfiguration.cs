using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Orders;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class FulfillmentAttemptConfiguration : IEntityTypeConfiguration<FulfillmentAttempt>
{
    public void Configure(EntityTypeBuilder<FulfillmentAttempt> builder)
    {
        builder.ToTable("FulfillmentAttempts", t =>
        {
            t.HasCheckConstraint("CK_FulfillmentAttempts_Status", "[Status] IN (1, 2, 3, 4, 5, 6)");
            t.HasCheckConstraint("CK_FulfillmentAttempts_AttemptNumber", "[AttemptNumber] IS NULL OR ([AttemptNumber] >= 1 AND [AttemptNumber] <= 3)");
            t.HasCheckConstraint("CK_FulfillmentAttempts_IsActive_Status", "([IsActive] = 1 AND [Status] IN (1, 2)) OR ([IsActive] = 0 AND [Status] IN (3, 4, 5, 6))");
            t.HasCheckConstraint("CK_FulfillmentAttempts_FailureCategory", "[FailureCategory] IS NULL OR [FailureCategory] IN (1, 2, 3)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.StoreId, x.OrderId, x.Id }).HasName("AK_FulfillmentAttempts_StoreId_OrderId_Id");

        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.AttemptNumber);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.PreparedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.DispatchedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.DeliveredAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.FailedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.CancelledAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.FailureReasonCode).HasConversion<int?>();
        builder.Property(x => x.FailureCategory).HasConversion<int?>();
        builder.Property(x => x.FailureNote).HasMaxLength(500);
        builder.Property(x => x.ResolvedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.ResolvedByUserId);
        builder.Property(x => x.ResolutionNote).HasMaxLength(500);
        builder.Property(x => x.RetryIdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.RetryRequestHash).HasColumnType("char(64)");

        builder.HasIndex(x => new { x.StoreId, x.OrderId })
            .IsUnique()
            .HasDatabaseName("UX_FulfillmentAttempts_StoreId_OrderId_Active")
            .HasFilter("[IsActive] = 1");

        builder.HasIndex(x => new { x.StoreId, x.OrderId, x.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("UX_FulfillmentAttempts_StoreId_OrderId_AttemptNumber")
            .HasFilter("[AttemptNumber] IS NOT NULL");

        builder.HasIndex(x => new { x.StoreId, x.OrderId, x.RetryIdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_FulfillmentAttempts_StoreId_OrderId_RetryIdempotencyKey")
            .HasFilter("[RetryIdempotencyKey] IS NOT NULL");

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => new { x.StoreId, x.OrderId, x.FulfillmentAttemptId })
            .HasPrincipalKey(x => new { x.StoreId, x.OrderId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FulfillmentAttemptItemConfiguration : IEntityTypeConfiguration<FulfillmentAttemptItem>
{
    public void Configure(EntityTypeBuilder<FulfillmentAttemptItem> builder)
    {
        builder.ToTable("FulfillmentAttemptItems", t =>
        {
            t.HasCheckConstraint("CK_FulfillmentAttemptItems_ReturnDisposition", "[ReturnDisposition] IN (0, 1, 2, 3, 4)");
            t.HasCheckConstraint("CK_FulfillmentAttemptItems_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint(
                "CK_FulfillmentAttemptItems_ReturnedAtUtc_Semantics",
                "(([ReturnDisposition] IN (2, 3) AND [ReturnedAtUtc] IS NOT NULL) OR ([ReturnDisposition] IN (0, 1, 4) AND [ReturnedAtUtc] IS NULL))");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasAlternateKey(x => new { x.StoreId, x.Id }).HasName("AK_FulfillmentAttemptItems_StoreId_Id");

        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.FulfillmentAttemptId).IsRequired();
        builder.Property(x => x.OrderItemId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.ReturnDisposition).HasConversion<int>().IsRequired();
        builder.Property(x => x.ReturnedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.ResolvedAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.ResolvedByUserId);
        builder.Property(x => x.ResolutionNote).HasMaxLength(500);
        builder.Property<byte[]>("RowVersion").IsRowVersion();

        builder.HasIndex(x => new { x.StoreId, x.FulfillmentAttemptId, x.OrderItemId })
            .IsUnique()
            .HasDatabaseName("UX_FulfillmentAttemptItems_Attempt_OrderItem");

        builder.HasOne<OrderItem>()
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.OrderId, x.OrderItemId })
            .HasPrincipalKey(x => new { x.StoreId, x.OrderId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
