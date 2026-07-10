namespace Cart.Domain;

public sealed class CartItem
{
    private CartItem() { }

    internal CartItem(Guid productId, string name, Money unitPrice, int quantity)
    {
        if (productId == Guid.Empty) throw new DomainException("invalid_product", "Product id is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("invalid_name", "Product name is required.");
        ProductId = productId;
        Name = name.Trim();
        UnitPriceAmount = unitPrice.Amount;
        Currency = unitPrice.Currency;
        SetQuantity(quantity);
    }

    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal UnitPriceAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public Money UnitPrice => new(UnitPriceAmount, Currency);
    public Money LineTotal => UnitPrice.Multiply(Quantity);

    internal void Add(int quantity) => SetQuantity(checked(Quantity + quantity));

    internal void SetQuantity(int quantity)
    {
        if (quantity is < 1 or > 999)
            throw new DomainException("invalid_quantity", "Quantity must be between 1 and 999.");
        Quantity = quantity;
    }
}
