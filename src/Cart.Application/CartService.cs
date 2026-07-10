using System.Security.Cryptography;
using Cart.Domain;

namespace Cart.Application;

public sealed class CartService(ICartRepository repository, IIdempotencyStore idempotency, ICartCache cache,
    ITokenService tokens, IClock clock)
{
    public async Task<CreatedCart> Create(CancellationToken ct)
    {
        var rawToken = tokens.Create();
        var cart = new ShoppingCart(Guid.NewGuid(), tokens.Hash(rawToken), clock.UtcNow);
        await repository.Add(cart, ct);
        await repository.Save(ct);
        var dto = Map(cart);
        await cache.Set(dto, ct);
        return new(dto, rawToken);
    }

    public async Task<CartDto> Get(Guid id, string token, CancellationToken ct)
    {
        var cart = await Authorized(id, token, ct);
        var cached = await cache.Get(id, ct);
        if (cached is not null && cached.Version == cart.Version) return cached;
        var dto = Map(cart);
        await cache.Set(dto, ct);
        return dto;
    }

    public Task<CartDto> Add(Guid id, string token, AddItem item, long version, string key, CancellationToken ct) =>
        Mutate(id, token, version, key, c => c.AddItem(item.ProductId, item.Name,
            new Money(item.UnitPrice, item.Currency), item.Quantity, clock.UtcNow), ct);

    public Task<CartDto> SetQuantity(Guid id, string token, Guid productId, int quantity, long version, string key,
        CancellationToken ct) => Mutate(id, token, version, key,
            c => c.SetQuantity(productId, quantity, clock.UtcNow), ct);

    public Task<CartDto> Remove(Guid id, string token, Guid productId, long version, string key, CancellationToken ct) =>
        Mutate(id, token, version, key, c => c.RemoveItem(productId, clock.UtcNow), ct);

    public Task<CartDto> Clear(Guid id, string token, long version, string key, CancellationToken ct) =>
        Mutate(id, token, version, key, c => c.Clear(clock.UtcNow), ct);

    private async Task<CartDto> Mutate(Guid id, string token, long version, string key, Action<ShoppingCart> action,
        CancellationToken ct)
    {
        var cart = await Authorized(id, token, ct);
        if (await idempotency.Exists(key, id, ct)) return Map(cart);
        if (cart.Version != version) throw new CartConcurrencyException();
        action(cart);
        await idempotency.Record(key, id, ct);
        await repository.Save(ct);
        await cache.Remove(id, ct);
        return Map(cart);
    }

    private async Task<ShoppingCart> Authorized(Guid id, string rawToken, CancellationToken ct)
    {
        var cart = await repository.Find(id, ct) ?? throw new CartNotFoundException();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(cart.AccessTokenHash), Convert.FromHexString(tokens.Hash(rawToken))))
            throw new CartAccessDeniedException();
        return cart;
    }

    public static CartDto Map(ShoppingCart cart) => new(cart.Id,
        cart.Items.Select(x => new CartItemDto(x.ProductId, x.Name, x.UnitPriceAmount, x.Quantity, x.LineTotal.Amount)).ToArray(),
        cart.Subtotal, cart.Currency, cart.Version, cart.CreatedAt, cart.UpdatedAt);
}
