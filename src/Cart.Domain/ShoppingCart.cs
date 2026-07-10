using System.Diagnostics;

namespace Cart.Domain;

public sealed class ShoppingCart
{
    public const int MaximumDistinctItems = 100;
    private readonly List<CartItem> _items = [];
    private ShoppingCart() { }

    public ShoppingCart(Guid id, string accessToken, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new DomainException("invalid_cart", "Cart id is required.");
        if (string.IsNullOrWhiteSpace(accessToken)) throw new DomainException("invalid_token", "Access token is required.");
        Id = id;
        AccessTokenHash = accessToken;
        CreatedAt = UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string AccessTokenHash { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<CartItem> Items => _items;
    public string? Currency => _items.Count == 0 ? null : _items[0].Currency;
    public decimal Subtotal => _items.Sum(x => x.LineTotal.Amount);

    public void AddItem(Guid productId, string name, Money unitPrice, int quantity, DateTimeOffset now)
    {
        EnsureCurrency(unitPrice.Currency);
        var existing = _items.SingleOrDefault(x => x.ProductId == productId);
        if (existing is null)
        {
            if (_items.Count >= MaximumDistinctItems)
                throw new DomainException("cart_item_limit", $"A cart cannot contain more than {MaximumDistinctItems} distinct items.");
            _items.Add(new CartItem(productId, name, unitPrice, quantity));
        }
        else
        {
            if (existing.UnitPrice != unitPrice)
                throw new DomainException("price_changed", "The product already exists with a different price.");
            existing.Add(quantity);
        }
        Touch(now);
    }

    public void SetQuantity(Guid productId, int quantity, DateTimeOffset now)
    {
        Item(productId).SetQuantity(quantity);
        Touch(now);
    }

    public void RemoveItem(Guid productId, DateTimeOffset now)
    {
        if (!_items.Remove(Item(productId))) throw new UnreachableException();
        Touch(now);
    }

    public void Clear(DateTimeOffset now)
    {
        _items.Clear();
        Touch(now);
    }

    private CartItem Item(Guid id) => _items.SingleOrDefault(x => x.ProductId == id)
        ?? throw new DomainException("item_not_found", "Cart item was not found.");
    private void EnsureCurrency(string currency)
    {
        if (Currency is not null && Currency != currency)
            throw new DomainException("mixed_currency", "A cart cannot contain multiple currencies.");
    }
    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = checked(Version + 1);
    }
}
