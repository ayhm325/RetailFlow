using Microsoft.EntityFrameworkCore;

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

public sealed class SessionTicketDbContext : DbContext
{
    public SessionTicketDbContext(
        DbContextOptions<SessionTicketDbContext> options)
        : base(options)
    {
    }

    public DbSet<PersistedAuthenticationTicket> AuthenticationTickets =>
        Set<PersistedAuthenticationTicket>();

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

            // Supports future revocation of all sessions for a user
            // within a specific store.
            entity.HasIndex(ticket => new
            {
                ticket.UserId,
                ticket.StoreId,
                ticket.AccountKind
            });

            // Supports efficient expiration cleanup.
            entity.HasIndex(ticket => ticket.ExpiresUtc);
        });
    }
}
