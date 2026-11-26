using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class RetailFlowIdentityUserConfiguration : IEntityTypeConfiguration<RetailFlowIdentityUser>
{
    public void Configure(EntityTypeBuilder<RetailFlowIdentityUser> builder)
    {
        builder.ToTable("AspNetUsers");

        builder.Property(x => x.StoreId);
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.MfaEnrollmentStatus).HasConversion<int>().IsRequired();
        builder.Property(x => x.MfaPendingExpiresAtUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.ProtectedPendingAuthenticatorKey).HasMaxLength(1024);
        builder.Property(x => x.MfaFailedAttempts).IsRequired();
        builder.Property(x => x.MfaLockoutEndUtc).HasColumnType("datetimeoffset(3)");
        builder.Property(x => x.LastUsedTotpTimeStep);

        // Tenant-aware email uniqueness: same email is allowed across different stores,
        // but must be unique within a store and globally unique for StoreId IS NULL (PlatformAdmin).
        builder.HasIndex(x => new { x.StoreId, x.NormalizedEmail })
            .IsUnique()
            .HasFilter("[StoreId] IS NOT NULL")
            .HasDatabaseName("UX_AspNetUsers_StoreId_NormalizedEmail");

        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique()
            .HasFilter("[StoreId] IS NULL")
            .HasDatabaseName("UX_AspNetUsers_Platform_NormalizedEmail");
    }
}

internal sealed class SessionTicketConfiguration : IEntityTypeConfiguration<SessionTicket>
{
    public void Configure(EntityTypeBuilder<SessionTicket> builder)
    {
        builder.ToTable("SessionTickets");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(256).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.StoreId);
        builder.Property(x => x.Value).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.LastSeenAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(x => x.AbsoluteExpiresAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();

        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("IX_SessionTickets_ExpiresAtUtc");

        builder.HasIndex(x => x.AbsoluteExpiresAtUtc)
            .HasDatabaseName("IX_SessionTickets_AbsoluteExpiresAtUtc");
    }
}
