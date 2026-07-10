using System.Threading.RateLimiting;
using Cart.Api;
using Cart.Application;
using Cart.Domain;
using Cart.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
if (int.TryParse(builder.Configuration["PORT"], out var renderPort))
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<CartService>();
var allowedOrigins = (builder.Configuration["AllowedOrigins"] ?? builder.Configuration["AllowedOrigin"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetTokenBucketLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new TokenBucketRateLimiterOptions { TokenLimit = 100, TokensPerPeriod = 100, ReplenishmentPeriod = TimeSpan.FromSeconds(10), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddHealthChecks().AddDbContextCheck<CartDbContext>(tags: ["ready"]);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("cart-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddPrometheusExporter();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) m.AddOtlpExporter();
    });

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseSwagger();
app.UseSwaggerUI();
app.MapOpenApi();
app.MapPrometheusScrapingEndpoint("/metrics");

app.MapPost("/api/v1/carts", async (CartService service, CancellationToken ct) =>
{
    var created = await service.Create(ct);
    return Results.Created($"/api/v1/carts/{created.Cart.Id}", created);
}).Produces<CreatedCart>(201).WithTags("Carts");

app.MapGet("/api/v1/carts/{cartId:guid}", async (Guid cartId, HttpRequest request, CartService service, CancellationToken ct) =>
    Results.Ok(await service.Get(cartId, Token(request), ct))).Produces<CartDto>().WithTags("Carts");

app.MapPost("/api/v1/carts/{cartId:guid}/items", async (Guid cartId, AddItemRequest body, HttpRequest request, CartService service, CancellationToken ct) =>
    Results.Ok(await service.Add(cartId, Token(request), new AddItem(body.ProductId, body.Name, body.UnitPrice, body.Currency, body.Quantity), body.Version, IdempotencyKey(request), ct)))
    .Produces<CartDto>().WithTags("Cart items");

app.MapPut("/api/v1/carts/{cartId:guid}/items/{productId:guid}", async (Guid cartId, Guid productId, SetQuantityRequest body, HttpRequest request, CartService service, CancellationToken ct) =>
    Results.Ok(await service.SetQuantity(cartId, Token(request), productId, body.Quantity, body.Version, IdempotencyKey(request), ct)))
    .Produces<CartDto>().WithTags("Cart items");

app.MapDelete("/api/v1/carts/{cartId:guid}/items/{productId:guid}", async (Guid cartId, Guid productId, long version, HttpRequest request, CartService service, CancellationToken ct) =>
    Results.Ok(await service.Remove(cartId, Token(request), productId, version, IdempotencyKey(request), ct)))
    .Produces<CartDto>().WithTags("Cart items");

app.MapDelete("/api/v1/carts/{cartId:guid}/items", async (Guid cartId, long version, HttpRequest request, CartService service, CancellationToken ct) =>
    Results.Ok(await service.Clear(cartId, Token(request), version, IdempotencyKey(request), ct)))
    .Produces<CartDto>().WithTags("Cart items");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("ApplyMigrations", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CartDbContext>().Database.MigrateAsync();
}

app.Run();

static string Token(HttpRequest request) => RequiredHeader(request, "X-Cart-Token");
static string IdempotencyKey(HttpRequest request)
{
    var value = RequiredHeader(request, "Idempotency-Key");
    if (value.Length > 100) throw new DomainException("invalid_idempotency_key", "Idempotency-Key cannot exceed 100 characters.");
    return value;
}
static string RequiredHeader(HttpRequest request, string name) => request.Headers[name].FirstOrDefault()
    ?? throw new DomainException("missing_header", $"{name} header is required.");

public partial class Program;
