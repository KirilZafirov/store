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
        idem.Property(x => x.Operation).HasMaxLength(50).IsRequired();
        idem.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        idem.Property(x => x.ResponseJson).HasColumnType("jsonb").IsRequired();
        idem.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class IdempotencyRecord
{
    public Guid CartId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
