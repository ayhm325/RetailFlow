using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Catalog;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StoreId)
            .IsRequired();

        builder.Property(x => x.ProductId)
            .IsRequired();

        builder.Property(x => x.ImageUrl)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.AltText)
            .HasMaxLength(250);

        builder.Property(x => x.SortOrder)
            .IsRequired();

        builder.HasOne<Product>()
            .WithMany(x => x.Images)
            .HasForeignKey(x => new { x.StoreId, x.ProductId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .HasConstraintName("FK_ProductImages_Products_StoreId_ProductId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.ProductId, x.SortOrder, x.Id })
            .HasDatabaseName("IX_ProductImages_StoreId_ProductId_SortOrder_Id");
    }
}
