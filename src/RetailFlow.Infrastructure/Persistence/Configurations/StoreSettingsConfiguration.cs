using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class StoreSettingsConfiguration : IEntityTypeConfiguration<StoreSettings>
{
    public void Configure(EntityTypeBuilder<StoreSettings> builder)
    {
        builder.ToTable("StoreSettings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StoreId)
            .IsRequired();

        builder.Property(x => x.CurrencyCode)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.TimeZoneId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.DeliveryEnabled)
            .IsRequired();

        builder.Property(x => x.PickupEnabled)
            .IsRequired();

        builder.Property(x => x.PublicAddress)
            .HasMaxLength(500);

        builder.Property(x => x.FulfillmentAddress)
            .HasMaxLength(500);

        builder.HasOne<Store>()
            .WithOne(x => x.Settings)
            .HasForeignKey<StoreSettings>(x => x.StoreId)
            .HasConstraintName("FK_StoreSettings_Stores_StoreId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Currency)
            .WithMany()
            .HasForeignKey(x => x.CurrencyCode)
            .HasConstraintName("FK_StoreSettings_Currencies_CurrencyCode")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.StoreId)
            .IsUnique()
            .HasDatabaseName("UX_StoreSettings_StoreId");
    }
}
