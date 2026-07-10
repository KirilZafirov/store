using Cart.Domain;

namespace Cart.Application;

public interface ICartRepository
{
    Task<ShoppingCart?> Find(Guid id, CancellationToken cancellationToken);
    Task Add(ShoppingCart cart, CancellationToken cancellationToken);
    Task Save(CancellationToken cancellationToken);
}

public interface IIdempotencyStore
{
    Task PruneExpired(DateTimeOffset now, CancellationToken cancellationToken);
    Task<IdempotencyEntry?> Find(Guid cartId, string key, CancellationToken cancellationToken);
    void Stage(IdempotencyEntry entry);
    Task<IdempotencyEntry?> Recover(Guid cartId, string key, CancellationToken cancellationToken);
}

public interface ICartCache
{
    Task<CartDto?> Get(Guid id, CancellationToken cancellationToken);
    Task Set(CartDto cart, CancellationToken cancellationToken);
    Task Remove(Guid id, CancellationToken cancellationToken);
}

public interface ITokenService
{
    string Create();
    string Hash(string token);
}

public interface IClock { DateTimeOffset UtcNow { get; } }

public sealed record IdempotencyEntry(Guid CartId, string Key, string Operation, string RequestHash,
    string ResponseJson, int StatusCode, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
