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
}).Produces<CreatedCart>(201).ProducesProblem(429).ProducesProblem(500).WithTags("Carts");

app.MapGet("/api/v1/carts/{cartId:guid}", async (Guid cartId, HttpRequest request, CartService service, CancellationToken ct) =>
    Results.Ok(await service.Get(RequestValidation.RequiredId(cartId, "cartId"), Token(request), ct)))
    .Produces<CartDto>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(500).WithTags("Carts");

app.MapPost("/api/v1/carts/{cartId:guid}/items", async (Guid cartId, AddItemRequest body, HttpRequest request, CartService service, CancellationToken ct) =>
    {
        body = RequestValidation.Validate(body);
        return Results.Ok(await service.Add(RequestValidation.RequiredId(cartId, "cartId"), Token(request),
            new AddItem(body.ProductId, body.Name, body.UnitPrice, body.Currency, body.Quantity), body.Version,
            IdempotencyKey(request), ct));
    }).Produces<CartDto>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).WithTags("Cart items");

app.MapPut("/api/v1/carts/{cartId:guid}/items/{productId:guid}", async (Guid cartId, Guid productId, SetQuantityRequest body, HttpRequest request, CartService service, CancellationToken ct) =>
    {
        body = RequestValidation.Validate(body);
        return Results.Ok(await service.SetQuantity(RequestValidation.RequiredId(cartId, "cartId"), Token(request),
            RequestValidation.RequiredId(productId, "productId"), body.Quantity, body.Version, IdempotencyKey(request), ct));
    }).Produces<CartDto>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).WithTags("Cart items");

app.MapDelete("/api/v1/carts/{cartId:guid}/items/{productId:guid}", async (Guid cartId, Guid productId, long version, HttpRequest request, CartService service, CancellationToken ct) =>
    Results.Ok(await service.Remove(RequestValidation.RequiredId(cartId, "cartId"), Token(request),
        RequestValidation.RequiredId(productId, "productId"), NonNegativeVersion(version), IdempotencyKey(request), ct)))
    .Produces<CartDto>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).WithTags("Cart items");

app.MapDelete("/api/v1/carts/{cartId:guid}/items", async (Guid cartId, long version, HttpRequest request, CartService service, CancellationToken ct) =>
    Results.Ok(await service.Clear(RequestValidation.RequiredId(cartId, "cartId"), Token(request),
        NonNegativeVersion(version), IdempotencyKey(request), ct)))
    .Produces<CartDto>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(500).WithTags("Cart items");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("ApplyMigrations", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CartDbContext>().Database.MigrateAsync();
}

app.Run();

static string Token(HttpRequest request) => request.Headers["X-Cart-Token"].FirstOrDefault()
    ?? throw new CartAccessDeniedException();
static string IdempotencyKey(HttpRequest request)
{
    var value = RequiredHeader(request, "Idempotency-Key");
    if (string.IsNullOrWhiteSpace(value) || value.Length > 100
        || !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':'))
        throw new RequestValidationException(new Dictionary<string, string[]>
        { ["idempotencyKey"] = ["Idempotency-Key must contain 1-100 letters, digits, '.', '_', ':' or '-'."] });
    return value;
}
static string RequiredHeader(HttpRequest request, string name) => request.Headers[name].FirstOrDefault()
    ?? throw new RequestValidationException(new Dictionary<string, string[]> { [name] = [$"{name} header is required."] });
static long NonNegativeVersion(long version) => version >= 0 ? version : throw new RequestValidationException(
    new Dictionary<string, string[]> { ["version"] = ["Version must be zero or greater."] });

public partial class Program;
