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
    Task<bool> Exists(string key, Guid cartId, CancellationToken cancellationToken);
    Task Record(string key, Guid cartId, CancellationToken cancellationToken);
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
