using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Cart.Api;

public static class RateLimitingConfiguration
{
    public const string CreateCartPolicy = "cart-create";
    public const string ProtectedCartPolicy = "cart-protected";

    public static IServiceCollection AddCartRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in Values(configuration, "ForwardedHeaders:KnownProxies"))
                if (IPAddress.TryParse(proxy, out var address)) options.KnownProxies.Add(address);

            foreach (var network in Values(configuration, "ForwardedHeaders:KnownNetworks"))
            {
                if (System.Net.IPNetwork.TryParse(network, out var ipNetwork))
                    options.KnownIPNetworks.Add(ipNetwork);
            }
        });

        services.AddRateLimiter(options =>
        {
            options.OnRejected = async (context, ct) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
                    ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))
                    : 1;
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "Too many cart requests were received. Retry after the indicated delay.",
                    Type = "https://atlas-cart.dev/problems/rate_limited",
                    Extensions = { ["code"] = "rate_limited", ["traceId"] = context.HttpContext.TraceIdentifier, ["retryAfterSeconds"] = retryAfter }
                }, ct);
            };

            options.AddPolicy(CreateCartPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                IpScope(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configuration.GetValue("RateLimits:CreateCart:PermitLimit", 10),
                    Window = TimeSpan.FromSeconds(configuration.GetValue("RateLimits:CreateCart:WindowSeconds", 60)),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));

            options.AddPolicy(ProtectedCartPolicy, context => RateLimitPartition.GetTokenBucketLimiter(
                CapabilityCartScope(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = configuration.GetValue("RateLimits:ProtectedCart:TokenLimit", 100),
                    TokensPerPeriod = configuration.GetValue("RateLimits:ProtectedCart:TokensPerPeriod", 100),
                    ReplenishmentPeriod = TimeSpan.FromSeconds(configuration.GetValue("RateLimits:ProtectedCart:ReplenishmentSeconds", 10)),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        });
        return services;
    }

    public static string IpScope(HttpContext context) =>
        $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    public static string CapabilityCartScope(HttpContext context)
    {
        var token = context.Request.Headers["X-Cart-Token"].FirstOrDefault() ?? string.Empty;
        var cartId = context.Request.RouteValues.TryGetValue("cartId", out var routeValue)
            ? Convert.ToString(routeValue, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
        return "cart:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{cartId}:{token}")))[..32];
    }

    private static IEnumerable<string> Values(IConfiguration configuration, string key) =>
        configuration.GetSection(key).Get<string[]>() ?? [];
}
