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
        if (exception is RequestValidationException validation)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ValidationProblemDetails(validation.Errors.ToDictionary(x => x.Key, x => x.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Request validation failed",
                    Detail = "One or more request values are invalid.",
                    Type = ProblemType("validation_error"),
                    Extensions = { ["code"] = "validation_error", ["traceId"] = context.TraceIdentifier }
                },
                Exception = exception
            });
        }

        var (status, title, detail, code) = exception switch
        {
            CartNotFoundException or CartAccessDeniedException or FormatException =>
                (404, "Cart not found", "The requested cart is unavailable.", "cart_not_found"),
            CartConcurrencyException => (409, "Cart was changed", "Reload the cart and retry with its latest version.", "concurrency_conflict"),
            IdempotencyKeyReusedException => (409, "Idempotency key was reused", "Use a new Idempotency-Key for a different request.", "idempotency_key_reused"),
            DomainException d => (400, "Cart request rejected", d.Message, d.Code),
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
                Type = ProblemType(code),
                Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
            },
            Exception = exception
        });
    }

    private static string ProblemType(string code) => $"https://atlas-cart.dev/problems/{code}";
}
