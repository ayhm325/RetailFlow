using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

public sealed class SessionTicketDbContext
    : DbContext, IDataProtectionKeyContext
{
    public SessionTicketDbContext(
        DbContextOptions<SessionTicketDbContext> options)
        : base(options)
    {
    }

    public DbSet<PersistedAuthenticationTicket> AuthenticationTickets =>
        Set<PersistedAuthenticationTicket>();

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PersistedAuthenticationTicket>(entity =>
        {
            entity.ToTable("AuthenticationSessionTickets");

            entity.HasKey(ticket => ticket.Key);

            entity.Property(ticket => ticket.Key)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(ticket => ticket.UserId)
                .HasMaxLength(450)
                .IsRequired();

            entity.Property(ticket => ticket.AccountKind)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(ticket => ticket.ProtectedTicket)
                .HasColumnType("varbinary(max)")
                .IsRequired();

            entity.Property(ticket => ticket.CreatedUtc)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            entity.Property(ticket => ticket.LastRenewedUtc)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            entity.Property(ticket => ticket.ExpiresUtc)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            entity.Property(ticket => ticket.AbsoluteExpiresUtc)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            entity.HasIndex(ticket => new
            {
                ticket.UserId,
                ticket.StoreId,
                ticket.AccountKind
            });

            entity.HasIndex(ticket => ticket.ExpiresUtc);

            entity.HasIndex(ticket => ticket.AbsoluteExpiresUtc);
        });

        // Shared ASP.NET Core Data Protection Key Ring.
        modelBuilder.Entity<DataProtectionKey>(entity =>
        {
            entity.ToTable("DataProtectionKeys");
        });
    }
}
