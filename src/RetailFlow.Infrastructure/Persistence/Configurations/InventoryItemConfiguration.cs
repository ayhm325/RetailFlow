using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Inventory;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems", table =>
        {
            table.HasCheckConstraint(
                "CK_InventoryItems_OnHand_NonNegative",
                "[OnHand] >= 0");

            table.HasCheckConstraint(
                "CK_InventoryItems_Reserved_NonNegative",
                "[Reserved] >= 0");

            table.HasCheckConstraint(
                "CK_InventoryItems_Reserved_NotExceed_OnHand",
                "[Reserved] <= [OnHand]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StoreId)
            .IsRequired();

        builder.Property(x => x.ProductId)
            .IsRequired();

        builder.Property(x => x.OnHand)
            .IsRequired();

        builder.Property(x => x.Reserved)
            .IsRequired();

        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsRequired();

        builder.Ignore(x => x.Available);
        builder.Ignore(x => x.IsAvailable);

        builder.HasOne<Product>()
            .WithOne(x => x.InventoryItem)
            .HasForeignKey<InventoryItem>(x => new { x.StoreId, x.ProductId })
            .HasPrincipalKey<Product>(x => new { x.StoreId, x.Id })
            .HasConstraintName("FK_InventoryItems_Products_StoreId_ProductId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("UX_InventoryItems_StoreId_ProductId");
    }
}
