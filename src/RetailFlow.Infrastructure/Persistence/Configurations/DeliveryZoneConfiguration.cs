using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Fulfillment;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("DeliveryZones", table =>
        {
            table.HasCheckConstraint(
                "CK_DeliveryZones_FlatFee_NonNegative",
                "[FlatFee] >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StoreId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.NormalizedName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.FlatFee)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(x => x.IsEnabled)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasPrecision(3)
            .IsRequired();

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("FK_DeliveryZones_Stores_StoreId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.NormalizedName })
            .IsUnique()
            .HasDatabaseName("UX_DeliveryZones_StoreId_NormalizedName");
    }
}
