using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Identity;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers", t =>
            t.HasCheckConstraint("CK_AppUsers_Role", "[Role] IN (1, 2, 3)"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.StoreId);
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Role).HasConversion<int>().IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(3)").IsRequired();

        builder.HasIndex(x => new { x.StoreId, x.NormalizedEmail })
            .IsUnique()
            .HasFilter("[StoreId] IS NOT NULL")
            .HasDatabaseName("UX_AppUsers_StoreId_NormalizedEmail");

        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique()
            .HasFilter("[StoreId] IS NULL")
            .HasDatabaseName("UX_AppUsers_Platform_NormalizedEmail");
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

        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("IX_SessionTickets_ExpiresAtUtc");
    }
}
