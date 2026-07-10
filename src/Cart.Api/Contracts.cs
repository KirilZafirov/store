using System.ComponentModel.DataAnnotations;

namespace Cart.Api;

public sealed record AddItemRequest(
    Guid ProductId,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: Range(typeof(decimal), "0", "99999999999999999")] decimal UnitPrice,
    [property: Required, RegularExpression("^[A-Za-z]{3}$")] string Currency,
    [property: Range(1, 999)] int Quantity,
    [property: Range(0, long.MaxValue)] long Version);

public sealed record SetQuantityRequest(
    [property: Range(1, 999)] int Quantity,
    [property: Range(0, long.MaxValue)] long Version);
