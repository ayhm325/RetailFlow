using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RetailFlow.Domain.Currencies;

namespace RetailFlow.Infrastructure.Persistence.Configurations;

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies", table =>
        {
            table.HasCheckConstraint(
                "CK_Currencies_Code_Format",
                "LEN([Code]) = 3 AND [Code] COLLATE Latin1_General_BIN2 NOT LIKE '%[^A-Z]%'");

            table.HasCheckConstraint(
                "CK_Currencies_NumericCode_Format",
                "LEN([NumericCode]) = 3 AND [NumericCode] NOT LIKE '%[^0-9]%'");

            table.HasCheckConstraint(
                "CK_Currencies_MinorUnit_Range",
                "[MinorUnit] >= 0 AND [MinorUnit] <= 4");
        });

        builder.HasKey(x => x.Code);

        builder.Property(x => x.Code)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.NumericCode)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.MinorUnit)
            .HasColumnType("tinyint")
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.HasIndex(x => x.NumericCode)
            .IsUnique()
            .HasDatabaseName("UX_Currencies_NumericCode");

        // Source: SIX Financial Information ISO 4217 List One (reviewed 2026-11-25).
        builder.HasData(
            Currency.Create("JOD", "400", 3, isActive: true),
            Currency.Create("KWD", "414", 3, isActive: true),
            Currency.Create("SAR", "682", 2, isActive: true),
            Currency.Create("AED", "784", 2, isActive: true),
            Currency.Create("USD", "840", 2, isActive: true),
            Currency.Create("EUR", "978", 2, isActive: true),
            Currency.Create("GBP", "826", 2, isActive: true),
            Currency.Create("JPY", "392", 0, isActive: true));
    }
}
