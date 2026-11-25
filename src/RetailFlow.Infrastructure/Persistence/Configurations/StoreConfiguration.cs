using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("Stores", table =>
        {
            table.HasCheckConstraint(
                "CK_Stores_StoreType",
                "[StoreType] IN (1, 2)");

            table.HasCheckConstraint(
                "CK_Stores_Status",
                "[Status] IN (1, 2, 3, 4)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.StoreType)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(x => x.LogoUrl)
            .HasMaxLength(500);

        builder.Property(x => x.PrimaryColor)
            .HasMaxLength(32);

        builder.Property(x => x.ContactEmail)
            .HasMaxLength(256);

        builder.Property(x => x.ContactPhone)
            .HasMaxLength(32);

        builder.Property(x => x.CreatedAtUtc)
            .HasPrecision(3)
            .IsRequired();

        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Stores_Slug");
    }
}
