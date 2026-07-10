namespace Cart.Application;

public sealed record CartItemDto(Guid ProductId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);
public sealed record CartDto(Guid Id, IReadOnlyCollection<CartItemDto> Items, decimal Subtotal, string? Currency,
    long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreatedCart(CartDto Cart, string AccessToken);
public sealed record AddItem(Guid ProductId, string Name, decimal UnitPrice, string Currency, int Quantity);

public sealed class CartNotFoundException : Exception;
public sealed class CartAccessDeniedException : Exception;
public sealed class CartConcurrencyException : Exception;
public sealed class IdempotencyKeyReusedException : Exception;
public sealed class IdempotencyRaceException : Exception;
