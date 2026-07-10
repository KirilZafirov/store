using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cart.Domain;

namespace Cart.Application;

public sealed class CartService(ICartRepository repository, IIdempotencyStore idempotency, ICartCache cache,
    ITokenService tokens, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

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
        Mutate(id, token, version, key, "add_item", new
        {
            ProductId = item.ProductId.ToString("N"),
            Name = item.Name.Trim(),
            UnitPrice = decimal.Round(item.UnitPrice, 2, MidpointRounding.ToEven),
            Currency = item.Currency.ToUpperInvariant(),
            item.Quantity,
            Version = version
        }, c => c.AddItem(item.ProductId, item.Name,
            new Money(item.UnitPrice, item.Currency), item.Quantity, clock.UtcNow), ct);

    public Task<CartDto> SetQuantity(Guid id, string token, Guid productId, int quantity, long version, string key,
        CancellationToken ct) => Mutate(id, token, version, key, "set_quantity",
            new { ProductId = productId.ToString("N"), Quantity = quantity, Version = version },
            c => c.SetQuantity(productId, quantity, clock.UtcNow), ct);

    public Task<CartDto> Remove(Guid id, string token, Guid productId, long version, string key, CancellationToken ct) =>
        Mutate(id, token, version, key, "remove_item",
            new { ProductId = productId.ToString("N"), Version = version },
            c => c.RemoveItem(productId, clock.UtcNow), ct);

    public Task<CartDto> Clear(Guid id, string token, long version, string key, CancellationToken ct) =>
        Mutate(id, token, version, key, "clear", new { Version = version }, c => c.Clear(clock.UtcNow), ct);

    private async Task<CartDto> Mutate(Guid id, string token, long version, string key, string operation,
        object request, Action<ShoppingCart> action, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var requestHash = Fingerprint(operation, request);
        await idempotency.PruneExpired(now, ct);
        var cart = await Authorized(id, token, ct);
        var replay = await idempotency.Find(id, key, ct);
        if (replay is not null) return Replay(replay, operation, requestHash);
        if (cart.Version != version) throw new CartConcurrencyException();
        action(cart);
        var response = Map(cart);
        idempotency.Stage(new IdempotencyEntry(id, key, operation, requestHash,
            JsonSerializer.Serialize(response, JsonOptions), 200, now, now.Add(IdempotencyRetention)));
        try
        {
            await repository.Save(ct);
        }
        catch (Exception exception) when (exception is CartConcurrencyException or IdempotencyRaceException)
        {
            var winner = await idempotency.Recover(id, key, ct);
            if (winner is not null) return Replay(winner, operation, requestHash);
            throw;
        }
        await cache.Remove(id, ct);
        return response;
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

    private static string Fingerprint(string operation, object request)
    {
        var json = JsonSerializer.Serialize(new { Operation = operation, Request = request }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static CartDto Replay(IdempotencyEntry entry, string operation, string requestHash)
    {
        if (entry.Operation != operation || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(entry.RequestHash), Convert.FromHexString(requestHash)))
            throw new IdempotencyKeyReusedException();
        return JsonSerializer.Deserialize<CartDto>(entry.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored idempotency response is invalid.");
    }
}
