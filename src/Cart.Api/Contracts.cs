using System.ComponentModel.DataAnnotations;
using Cart.Domain;

namespace Cart.Api;

public sealed record AddItemRequest(
    Guid ProductId,
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: Range(typeof(decimal), "0", "99999999999999999.99")] decimal UnitPrice,
    [property: Required, RegularExpression("^[A-Za-z]{3}$")] string Currency,
    [property: Range(1, 999)] int Quantity,
    [property: Range(0, long.MaxValue)] long Version) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProductId == Guid.Empty)
            yield return new ValidationResult("Product id is required.", [nameof(ProductId)]);
        if (Name is not null && Name != Name.Trim())
            yield return new ValidationResult("Product name cannot start or end with whitespace.", [nameof(Name)]);
        if (((decimal.GetBits(UnitPrice)[3] >> 16) & 0x7F) > 2)
            yield return new ValidationResult("Price cannot contain more than two decimal places.", [nameof(UnitPrice)]);
    }
}

public sealed record SetQuantityRequest(
    [property: Range(1, 999)] int Quantity,
    [property: Range(0, long.MaxValue)] long Version);

public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more request values are invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public static class RequestValidation
{
    public static T Validate<T>(T value)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(value!, new ValidationContext(value!), results, validateAllProperties: true))
            return value;

        var errors = results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty("request")
                .Select(member => new { Member = ToCamelCase(member), Message = result.ErrorMessage ?? "Invalid value." }))
            .GroupBy(x => x.Member)
            .ToDictionary(group => group.Key, group => group.Select(x => x.Message).Distinct().ToArray());
        throw new RequestValidationException(errors);
    }

    public static Guid RequiredId(Guid value, string field) => value != Guid.Empty
        ? value
        : throw new RequestValidationException(new Dictionary<string, string[]> { [field] = ["Identifier is required."] });

    private static string ToCamelCase(string value) => string.IsNullOrEmpty(value)
        ? value
        : char.ToLowerInvariant(value[0]) + value[1..];
}
