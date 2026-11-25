namespace RetailFlow.Domain.Currencies;

/// <summary>
/// Global ISO 4217 currency reference data. Not tenant-owned.
/// </summary>
public sealed class Currency
{
    public const byte MaxSupportedMinorUnit = 4;

    private Currency()
    {
        Code = string.Empty;
        NumericCode = string.Empty;
    }

    public string Code { get; private set; }

    public string NumericCode { get; private set; }

    public byte MinorUnit { get; private set; }

    public bool IsActive { get; private set; }

    public static Currency Create(
        string code,
        string numericCode,
        byte minorUnit,
        bool isActive = true)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedNumericCode = NormalizeNumericCode(numericCode);

        if (minorUnit > MaxSupportedMinorUnit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minorUnit),
                minorUnit,
                $"MinorUnit must be between 0 and {MaxSupportedMinorUnit}.");
        }

        return new Currency
        {
            Code = normalizedCode,
            NumericCode = normalizedNumericCode,
            MinorUnit = minorUnit,
            IsActive = isActive
        };
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Currency code is required.", nameof(code));
        }

        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException(
                "Currency code must be a 3-letter ISO 4217 alphabetic code.",
                nameof(code));
        }

        return trimmed;
    }

    public static string NormalizeNumericCode(string numericCode)
    {
        if (string.IsNullOrWhiteSpace(numericCode))
        {
            throw new ArgumentException("Currency numeric code is required.", nameof(numericCode));
        }

        var trimmed = numericCode.Trim();
        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "Currency numeric code must be a 3-digit ISO 4217 numeric code.",
                nameof(numericCode));
        }

        return trimmed;
    }
}
