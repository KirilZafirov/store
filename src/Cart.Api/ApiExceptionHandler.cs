using Cart.Application;
using Cart.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Cart.Api;

public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail, code) = exception switch
        {
            CartNotFoundException => (404, "Cart not found", "The requested cart does not exist.", "cart_not_found"),
            CartAccessDeniedException => (403, "Cart access denied", "The cart token is missing or invalid.", "cart_access_denied"),
            CartConcurrencyException => (409, "Cart was changed", "Reload the cart and retry with its latest version.", "concurrency_conflict"),
            IdempotencyKeyReusedException => (409, "Idempotency key was reused", "Use a new Idempotency-Key for a different request.", "idempotency_key_reused"),
            DomainException d => (400, "Cart request rejected", d.Message, d.Code),
            FormatException => (403, "Cart access denied", "The cart token is missing or invalid.", "cart_access_denied"),
            _ => (500, "Unexpected error", "An unexpected error occurred.", "internal_error")
        };

        if (status == 500) logger.LogError(exception, "Unhandled request failure");
        else logger.LogWarning("Request rejected with {Code}: {Message}", code, exception.Message);

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Type = $"https://example.com/problems/{code}",
                Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
            },
            Exception = exception
        });
    }
}
