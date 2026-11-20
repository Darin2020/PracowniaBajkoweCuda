namespace BajkoweCuda.Domain.Orders;

/// <summary>
/// A price (docs/PLAN.md §5: <c>Amount decimal(10,2)</c>, currency EUR). Always positive, with at most two decimals;
/// the studio sells in euros only.
/// </summary>
public readonly record struct Money
{
    public const string Euro = "EUR";
    public const decimal MaxAmount = 99_999_999.99m;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Euros(decimal amount)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException(OrderErrors.InvalidPrice, "A price is a positive amount with at most two decimals.");
        }

        return new Money(amount, Euro);
    }

    /// <summary>For the database: values that were valid when stored.</summary>
    public static Money FromStored(decimal amount, string currency) => new(amount, currency);

    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");
}
