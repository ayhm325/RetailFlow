namespace RetailFlow.Domain.Currencies;

/// <summary>
/// Validates monetary amounts against an ISO 4217 currency's MinorUnit without implicit rounding.
/// </summary>
public static class MoneyPrecision
{
    public static void EnsureValidAmount(
        decimal amount,
        byte minorUnit,
        string paramName = "amount")
    {
        if (minorUnit > Currency.MaxSupportedMinorUnit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minorUnit),
                minorUnit,
                $"MinorUnit must be between 0 and {Currency.MaxSupportedMinorUnit}.");
        }

        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                amount,
                "Monetary amount cannot be negative.");
        }

        if (!HasValidPrecision(amount, minorUnit))
        {
            throw new ArgumentException(
                $"Monetary amount {amount} exceeds the allowed {minorUnit} decimal places for the store currency.",
                paramName);
        }
    }

    public static bool HasValidPrecision(decimal amount, byte minorUnit)
    {
        if (amount < 0m || minorUnit > Currency.MaxSupportedMinorUnit)
        {
            return false;
        }

        var scaleFactor = minorUnit switch
        {
            0 => 1m,
            1 => 10m,
            2 => 100m,
            3 => 1000m,
            4 => 10000m,
            _ => throw new ArgumentOutOfRangeException(nameof(minorUnit))
        };

        if (amount > decimal.MaxValue / scaleFactor)
        {
            return false;
        }

        var scaled = amount * scaleFactor;
        return decimal.Truncate(scaled) == scaled;
    }
}
