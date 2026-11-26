using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class CheckoutRequestConfiguration : IEntityTypeConfiguration<CheckoutRequest>
{
    public void Configure(EntityTypeBuilder<CheckoutRequest> builder)
    {
        builder.ToTable("CheckoutRequests", t =>
            t.HasCheckConstraint("CK_CheckoutRequests_Status", "[Status] IN (1, 2, 3)"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RequestHash).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.OrderId);
        builder.Property(x => x.ResponseStatusCode);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();

        builder.HasIndex(x => new { x.StoreId, x.CustomerId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_CheckoutRequests_StoreId_CustomerId_IdempotencyKey");

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
