using Cart.Application;
using Cart.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "PK_cart_items"
        })
        { throw new CartConcurrencyException(); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "PK_idempotency_records"
        })
        { throw new IdempotencyRaceException(); }
    }
}

public sealed class IdempotencyStore(CartDbContext db) : IIdempotencyStore
{
    public Task PruneExpired(DateTimeOffset now, CancellationToken ct) =>
        db.IdempotencyRecords.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);

    public async Task<IdempotencyEntry?> Find(Guid cartId, string key, CancellationToken ct) =>
        Map(await db.IdempotencyRecords.SingleOrDefaultAsync(x => x.CartId == cartId && x.Key == key, ct));

    public void Stage(IdempotencyEntry entry)
    {
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            CartId = entry.CartId,
            Key = entry.Key,
            Operation = entry.Operation,
            RequestHash = entry.RequestHash,
            ResponseJson = entry.ResponseJson,
            StatusCode = entry.StatusCode,
            CreatedAt = entry.CreatedAt,
            ExpiresAt = entry.ExpiresAt
        });
    }

    public async Task<IdempotencyEntry?> Recover(Guid cartId, string key, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return Map(await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CartId == cartId && x.Key == key, ct));
    }

    private static IdempotencyEntry? Map(IdempotencyRecord? record) => record is null ? null :
        new(record.CartId, record.Key, record.Operation, record.RequestHash, record.ResponseJson,
            record.StatusCode, record.CreatedAt, record.ExpiresAt);
}
