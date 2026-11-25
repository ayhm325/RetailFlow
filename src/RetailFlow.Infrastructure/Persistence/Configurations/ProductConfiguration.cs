using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table =>
        {
            table.HasCheckConstraint(
                "CK_Products_Price_NonNegative",
                "[Price] >= 0");

            table.HasCheckConstraint(
                "CK_Products_Status",
                "[Status] IN (1, 2, 3)");
        });

        builder.HasKey(x => x.Id);

        builder.HasAlternateKey(x => new { x.StoreId, x.Id })
            .HasName("AK_Products_StoreId_Id");

        builder.Property(x => x.StoreId)
            .IsRequired();

        builder.Property(x => x.CategoryId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.Sku)
            .HasMaxLength(64);

        builder.Property(x => x.Price)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasPrecision(3)
            .IsRequired();

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("FK_Products_Stores_StoreId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.CategoryId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .HasConstraintName("FK_Products_Categories_StoreId_CategoryId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.Sku })
            .IsUnique()
            .HasFilter("[Sku] IS NOT NULL")
            .HasDatabaseName("UX_Products_StoreId_Sku");

        builder.HasIndex(x => new { x.StoreId, x.Status, x.CategoryId })
            .HasDatabaseName("IX_Products_StoreId_Status_CategoryId");

        builder.HasIndex(x => new { x.StoreId, x.Status, x.CreatedAtUtc, x.Id })
            .IsDescending(false, false, true, false)
            .HasDatabaseName("IX_Products_StoreId_Status_CreatedAtUtc_Id");

        builder.HasIndex(x => new { x.StoreId, x.Status, x.Price, x.Id })
            .HasDatabaseName("IX_Products_StoreId_Status_Price_Id");
    }
}
