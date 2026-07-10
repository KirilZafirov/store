namespace Cart.Domain;

public readonly record struct Money
{
    public const decimal MaximumAmount = 99999999999999999.99m;
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new DomainException("invalid_price", "Price cannot be negative.");
        if (Scale(amount) > 2)
            throw new DomainException("invalid_price_scale", "Price cannot contain more than two decimal places.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new DomainException("invalid_currency", "Currency must be a three-letter ISO code.");
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public Money Multiply(int quantity) => new(Amount * quantity, Currency);

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7F;
}
