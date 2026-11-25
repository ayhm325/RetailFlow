using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(x => x.Id);

        builder.HasAlternateKey(x => new { x.StoreId, x.Id })
            .HasName("AK_Categories_StoreId_Id");

        builder.Property(x => x.StoreId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.SortOrder)
            .IsRequired();

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasConstraintName("FK_Categories_Stores_StoreId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.Slug })
            .IsUnique()
            .HasDatabaseName("UX_Categories_StoreId_Slug");

        builder.HasIndex(x => new { x.StoreId, x.IsActive, x.SortOrder, x.Id })
            .HasDatabaseName("IX_Categories_StoreId_IsActive_SortOrder_Id");
    }
}
