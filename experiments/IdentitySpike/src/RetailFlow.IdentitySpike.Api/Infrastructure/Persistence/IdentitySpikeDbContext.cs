using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;

namespace RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

public sealed class IdentitySpikeDbContext
    : IdentityUserContext<ApplicationUser>
{
    public IdentitySpikeDbContext(
        DbContextOptions<IdentitySpikeDbContext> options)
        : base(options)
    {
    }

    public DbSet<SpikeStore> Stores => Set<SpikeStore>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SpikeStore>(entity =>
        {
            entity.ToTable("Stores");

            entity.HasKey(store => store.Id);

            entity.Property(store => store.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(store => store.Slug)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(store => store.Slug)
                .IsUnique();
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            // Enforce valid account type and store ownership.
            entity.ToTable(table =>
                table.HasCheckConstraint(
                    "CK_AspNetUsers_AccountKind_StoreId",
                    "([AccountKind] = 1 AND [StoreId] IS NULL) OR " +
                    "([AccountKind] = 2 AND [StoreId] IS NOT NULL)"
                ));

            // Allow the same email in different stores,
            // but prevent duplicates within the same store.
            entity.HasIndex(user => new
            {
                user.StoreId,
                user.NormalizedEmail
            })
                .IsUnique()
                .HasDatabaseName(
                    "UX_AspNetUsers_StoreId_NormalizedEmail");

            // Ensure BackOffice email is globally unique.
            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique()
                .HasFilter(
                    "[StoreId] IS NULL AND [NormalizedEmail] IS NOT NULL")
                .HasDatabaseName(
                    "UX_AspNetUsers_BackOffice_NormalizedEmail");

            // Link customer account to its store.
            entity.HasOne<SpikeStore>()
                .WithMany()
                .HasForeignKey(user => user.StoreId)
                .OnDelete(DeleteBehavior.Restrict);


            entity.Property(user => user.Email)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(user => user.NormalizedEmail)
                .HasMaxLength(256)
                .IsRequired();
        });
    }
}