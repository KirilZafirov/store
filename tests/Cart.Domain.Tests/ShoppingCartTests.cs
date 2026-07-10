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

    private static ShoppingCart NewCart() => new(Guid.NewGuid(), "hash", Now);
}
