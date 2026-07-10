using Cart.Domain;
using Xunit;

namespace Cart.Domain.Tests;

public sealed class ShoppingCartTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Adding_same_product_aggregates_quantity_and_total()
    {
        var cart = NewCart();
        var product = Guid.NewGuid();
        cart.AddItem(product, "Keyboard", new Money(49.95m, "eur"), 1, Now);
        cart.AddItem(product, "Keyboard", new Money(49.95m, "EUR"), 2, Now.AddMinutes(1));

        var item = Assert.Single(cart.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(149.85m, cart.Subtotal);
        Assert.Equal("EUR", cart.Currency);
    }

    [Fact]
    public void Mixed_currencies_are_rejected()
    {
        var cart = NewCart();
        cart.AddItem(Guid.NewGuid(), "Keyboard", new Money(50m, "EUR"), 1, Now);
        var error = Assert.Throws<DomainException>(() =>
            cart.AddItem(Guid.NewGuid(), "Mouse", new Money(20m, "USD"), 1, Now));
        Assert.Equal("mixed_currency", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void Invalid_quantity_is_rejected(int quantity)
    {
        var error = Assert.Throws<DomainException>(() =>
            NewCart().AddItem(Guid.NewGuid(), "Mouse", new Money(20m, "EUR"), quantity, Now));
        Assert.Equal("invalid_quantity", error.Code);
    }

    [Fact]
    public void Remove_and_clear_update_contents()
    {
        var cart = NewCart();
        var first = Guid.NewGuid();
        cart.AddItem(first, "A", new Money(1m, "EUR"), 1, Now);
        cart.AddItem(Guid.NewGuid(), "B", new Money(2m, "EUR"), 1, Now);
        cart.RemoveItem(first, Now.AddMinutes(1));
        Assert.Single(cart.Items);
        cart.Clear(Now.AddMinutes(2));
        Assert.Empty(cart.Items);
        Assert.Null(cart.Currency);
    }

    [Fact]
    public void Mutations_update_version_timestamps_totals_and_normalize_snapshots()
    {
        var cart = NewCart();
        var product = Guid.NewGuid();

        cart.AddItem(product, " Keyboard ", new Money(10m, "eur"), 2, Now.AddMinutes(1));
        var item = Assert.Single(cart.Items);
        Assert.Equal("Keyboard", item.Name);
        Assert.Equal("EUR", cart.Currency);
        Assert.Equal(20m, cart.Subtotal);
        Assert.Equal(1, cart.Version);
        Assert.Equal(Now.AddMinutes(1), cart.UpdatedAt);

        cart.SetQuantity(product, 3, Now.AddMinutes(2));
        Assert.Equal(30m, cart.Subtotal);
        Assert.Equal(2, cart.Version);
        Assert.Equal(Now.AddMinutes(2), cart.UpdatedAt);

        cart.RemoveItem(product, Now.AddMinutes(3));
        Assert.Empty(cart.Items);
        Assert.Null(cart.Currency);
        Assert.Equal(0m, cart.Subtotal);
        Assert.Equal(3, cart.Version);
        Assert.Equal(Now.AddMinutes(3), cart.UpdatedAt);
        Assert.Equal(Now, cart.CreatedAt);
    }

    [Fact]
    public void Missing_items_are_rejected_for_quantity_change_and_removal()
    {
        var cart = NewCart();

        Assert.Equal("item_not_found", Assert.Throws<DomainException>(() =>
            cart.SetQuantity(Guid.NewGuid(), 2, Now)).Code);
        Assert.Equal("item_not_found", Assert.Throws<DomainException>(() =>
            cart.RemoveItem(Guid.NewGuid(), Now)).Code);
    }

    [Fact]
    public void Invalid_money_and_product_snapshots_are_rejected()
    {
        Assert.Equal("invalid_currency", Assert.Throws<DomainException>(() => new Money(1m, "E1R")).Code);
        Assert.Equal("invalid_price_scale", Assert.Throws<DomainException>(() => new Money(1.001m, "EUR")).Code);

        var cart = NewCart();
        Assert.Equal("invalid_name", Assert.Throws<DomainException>(() =>
            cart.AddItem(Guid.NewGuid(), new string('x', 201), new Money(1m, "EUR"), 1, Now)).Code);
        Assert.Equal("invalid_price", Assert.Throws<DomainException>(() =>
            cart.AddItem(Guid.NewGuid(), "Product", new Money(Money.MaximumAmount + 0.01m, "EUR"), 1, Now)).Code);
    }

    [Fact]
    public void Distinct_item_limit_is_enforced_by_the_aggregate()
    {
        var cart = NewCart();
        for (var index = 0; index < ShoppingCart.MaximumDistinctItems; index++)
            cart.AddItem(Guid.NewGuid(), $"Product {index}", new Money(1m, "EUR"), 1, Now);

        var error = Assert.Throws<DomainException>(() =>
            cart.AddItem(Guid.NewGuid(), "One too many", new Money(1m, "EUR"), 1, Now));
        Assert.Equal("cart_item_limit", error.Code);
    }

    private static ShoppingCart NewCart() => new(Guid.NewGuid(), "hash", Now);
}
