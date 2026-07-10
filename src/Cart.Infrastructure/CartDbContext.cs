using Cart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cart.Infrastructure;

public sealed class CartDbContext(DbContextOptions<CartDbContext> options) : DbContext(options)
{
    public DbSet<ShoppingCart> Carts => Set<ShoppingCart>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var cart = modelBuilder.Entity<ShoppingCart>();
        cart.ToTable("carts");
        cart.HasKey(x => x.Id);
        cart.Property(x => x.AccessTokenHash).HasMaxLength(64).IsRequired();
        cart.Property(x => x.Version).IsConcurrencyToken();
        cart.Property(x => x.CreatedAt).IsRequired();
        cart.Property(x => x.UpdatedAt).IsRequired();
        cart.HasMany(x => x.Items).WithOne().HasForeignKey("CartId").OnDelete(DeleteBehavior.Cascade);
        cart.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        var item = modelBuilder.Entity<CartItem>();
        item.ToTable("cart_items");
        item.HasKey("CartId", nameof(CartItem.ProductId));
        item.Property(x => x.Name).HasMaxLength(200).IsRequired();
        item.Property(x => x.UnitPriceAmount).HasPrecision(19, 2);
        item.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        item.Ignore(x => x.UnitPrice);
        item.Ignore(x => x.LineTotal);

        var idem = modelBuilder.Entity<IdempotencyRecord>();
        idem.ToTable("idempotency_records");
        idem.HasKey(x => new { x.CartId, x.Key });
        idem.Property(x => x.Key).HasMaxLength(100);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ShoppingCart>().Where(e => e.State == EntityState.Modified))
            entry.Property(x => x.Version).CurrentValue++;
        return base.SaveChangesAsync(cancellationToken);
    }
}

public sealed class IdempotencyRecord
{
    public Guid CartId { get; init; }
    public string Key { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}
