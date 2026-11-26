using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.StoreId, x.Id }).HasName("AK_Carts_StoreId_Id");

        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(x => new { x.StoreId, x.CustomerId })
            .IsUnique()
            .HasDatabaseName("UX_Carts_StoreId_CustomerId");

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => new { x.StoreId, x.CartId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", t =>
            t.HasCheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StoreId).IsRequired();
        builder.Property(x => x.CartId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(x => new { x.StoreId, x.CartId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("UX_CartItems_StoreId_CartId_ProductId");
    }
}
