using Cart.Application;
using Cart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cart.Infrastructure;

public sealed class CartRepository(CartDbContext db) : ICartRepository
{
    public Task<ShoppingCart?> Find(Guid id, CancellationToken ct) =>
        db.Carts.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task Add(ShoppingCart cart, CancellationToken ct) => db.Carts.AddAsync(cart, ct).AsTask();
    public async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new CartConcurrencyException(); }
    }
}

public sealed class IdempotencyStore(CartDbContext db, TimeProvider timeProvider) : IIdempotencyStore
{
    public Task<bool> Exists(string key, Guid cartId, CancellationToken ct) =>
        db.IdempotencyRecords.AnyAsync(x => x.CartId == cartId && x.Key == key, ct);
    public Task Record(string key, Guid cartId, CancellationToken ct)
    {
        db.IdempotencyRecords.Add(new IdempotencyRecord { CartId = cartId, Key = key, CreatedAt = timeProvider.GetUtcNow() });
        return Task.CompletedTask;
    }
}
