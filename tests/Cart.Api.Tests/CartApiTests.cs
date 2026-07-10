using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cart.Application;
using Cart.Domain;
using Cart.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Cart.Api.Tests;

public sealed class CartApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient Client => _factory!.CreateClient();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CartDatabase"] = _postgres.GetConnectionString(),
                ["ApplyMigrations"] = "true"
            })));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Full_cart_flow_enforces_token_concurrency_and_idempotency()
    {
        var createdResponse = await Client.PostAsync("/api/v1/carts", null);
        var created = await createdResponse.Content.ReadFromJsonAsync<CreatedCart>();
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.NotNull(created);

        var productId = Guid.NewGuid();
        var add = new { productId, name = "Mechanical keyboard", unitPrice = 89.90m, currency = "EUR", quantity = 1, version = 0 };
        var request = Request(HttpMethod.Post, $"/api/v1/carts/{created.Cart.Id}/items", created.AccessToken, "add-1", add);
        var addedResponse = await Client.SendAsync(request);
        var added = await addedResponse.Content.ReadFromJsonAsync<CartDto>();
        Assert.Equal(HttpStatusCode.OK, addedResponse.StatusCode);
        Assert.Equal(1, added!.Version);

        var retry = await Client.SendAsync(Request(HttpMethod.Post, $"/api/v1/carts/{created.Cart.Id}/items", created.AccessToken, "add-1", add));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, (await retry.Content.ReadFromJsonAsync<CartDto>())!.Items.Single().Quantity);

        var stale = await Client.SendAsync(Request(HttpMethod.Put, $"/api/v1/carts/{created.Cart.Id}/items/{productId}", created.AccessToken, "update-1", new { quantity = 2, version = 0 }));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var hidden = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/carts/{created.Cart.Id}");
        hidden.Headers.Add("X-Cart-Token", new string('0', 64));
        Assert.Equal(HttpStatusCode.NotFound, (await Client.SendAsync(hidden)).StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_report_liveness_and_database_readiness()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Cart_creation_limit_returns_problem_details_and_retry_information()
    {
        await using var factory = FactoryWith(new Dictionary<string, string?>
        {
            ["RateLimits:CreateCart:PermitLimit"] = "1",
            ["RateLimits:CreateCart:WindowSeconds"] = "60"
        });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/v1/carts", null)).StatusCode);
        var limited = await client.PostAsync("/api/v1/carts", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta?.TotalSeconds >= 1 || limited.Headers.RetryAfter is not null);
        await AssertProblem(limited, "rate_limited", HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Protected_cart_limit_is_partitioned_by_non_reversible_cart_capability_scope()
    {
        var firstScope = new DefaultHttpContext();
        firstScope.Request.RouteValues["cartId"] = Guid.Parse("10000000-0000-0000-0000-000000000001");
        firstScope.Request.Headers["X-Cart-Token"] = "secret-token";
        var secondScope = new DefaultHttpContext();
        secondScope.Request.RouteValues["cartId"] = Guid.Parse("10000000-0000-0000-0000-000000000002");
        secondScope.Request.Headers["X-Cart-Token"] = "secret-token";

        var partition = RateLimitingConfiguration.CapabilityCartScope(firstScope);
        Assert.NotEqual(partition, RateLimitingConfiguration.CapabilityCartScope(secondScope));
        Assert.DoesNotContain("secret-token", partition);
        Assert.DoesNotContain("10000000", partition);

        await using var factory = FactoryWith(new Dictionary<string, string?>
        {
            ["RateLimits:ProtectedCart:TokenLimit"] = "1",
            ["RateLimits:ProtectedCart:TokensPerPeriod"] = "1",
            ["RateLimits:ProtectedCart:ReplenishmentSeconds"] = "60"
        });
        using var client = factory.CreateClient();
        var created = await CreateCart(client);

        var first = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/carts/{created.Cart.Id}");
        first.Headers.Add("X-Cart-Token", created.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(first)).StatusCode);

        var second = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/carts/{created.Cart.Id}");
        second.Headers.Add("X-Cart-Token", created.AccessToken);
        var limited = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await AssertProblem(limited, "rate_limited", HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Readiness_reports_unavailable_database()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CartDatabase"] = "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1;Command Timeout=1",
                ["ApplyMigrations"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CartDbContext>>();
                services.AddDbContext<CartDbContext>(options => options.UseNpgsql(
                    "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1;Command Timeout=1"));
            });
        });

        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Endpoint_contract_supports_create_get_add_aggregate_update_remove_and_clear()
    {
        var created = await CreateCart();
        var firstProduct = Guid.NewGuid();
        var secondProduct = Guid.NewGuid();

        var fetched = await AuthorizedGet(created);
        Assert.Empty(fetched.Items);
        Assert.Equal(0, fetched.Version);
        Assert.Null(fetched.Currency);

        var firstAdd = await AddItem(created, firstProduct, "Keyboard", 10m, "EUR", 1, 0, "flow-add-1");
        Assert.Equal(1, firstAdd.Version);
        Assert.Equal(10m, firstAdd.Subtotal);
        Assert.Equal("EUR", firstAdd.Currency);

        var aggregated = await AddItem(created, firstProduct, "Keyboard", 10m, "EUR", 2, 1, "flow-add-2");
        var aggregatedItem = Assert.Single(aggregated.Items);
        Assert.Equal(3, aggregatedItem.Quantity);
        Assert.Equal(30m, aggregatedItem.LineTotal);

        var withSecondItem = await AddItem(created, secondProduct, "Mouse", 5m, "EUR", 1, 2, "flow-add-3");
        Assert.Equal(2, withSecondItem.Items.Count);
        Assert.Equal(35m, withSecondItem.Subtotal);

        var updated = await SendCart(Request(HttpMethod.Put,
            $"/api/v1/carts/{created.Cart.Id}/items/{firstProduct}", created.AccessToken, "flow-update",
            new { quantity = 4, version = 3 }));
        Assert.Equal(4, updated.Items.Single(x => x.ProductId == firstProduct).Quantity);
        Assert.Equal(45m, updated.Subtotal);

        var removed = await SendCart(Request(HttpMethod.Delete,
            $"/api/v1/carts/{created.Cart.Id}/items/{secondProduct}?version=4", created.AccessToken, "flow-remove", new { }));
        Assert.Single(removed.Items);

        var cleared = await SendCart(Request(HttpMethod.Delete,
            $"/api/v1/carts/{created.Cart.Id}/items?version=5", created.AccessToken, "flow-clear", new { }));
        Assert.Empty(cleared.Items);
        Assert.Null(cleared.Currency);
        Assert.Equal(0m, cleared.Subtotal);
    }

    [Fact]
    public async Task Incompatible_product_snapshot_is_rejected()
    {
        var created = await CreateCart();
        var productId = Guid.NewGuid();

        await AddItem(created, productId, "Keyboard", 10m, "EUR", 1, 0, "snapshot-add");
        var changedPrice = await Client.SendAsync(Request(HttpMethod.Post,
            $"/api/v1/carts/{created.Cart.Id}/items", created.AccessToken, "snapshot-conflict",
            new { productId, name = "Keyboard", unitPrice = 11m, currency = "EUR", quantity = 1, version = 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, changedPrice.StatusCode);
        await AssertProblem(changedPrice, "price_changed", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Idempotency_key_replays_original_response_and_rejects_other_requests()
    {
        var created = await CreateCart();
        var productId = Guid.NewGuid();
        var uri = $"/api/v1/carts/{created.Cart.Id}/items";
        var add = new { productId, name = "Keyboard", unitPrice = 40m, currency = "EUR", quantity = 1, version = 0 };

        var first = await Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, "stable-key", add));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var original = await first.Content.ReadFromJsonAsync<CartDto>();

        var update = await Client.SendAsync(Request(HttpMethod.Put,
            $"{uri}/{productId}", created.AccessToken, "update-key", new { quantity = 2, version = 1 }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var replay = await Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, "stable-key", add));
        var replayed = await replay.Content.ReadFromJsonAsync<CartDto>();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        AssertCart(original!, replayed!);

        var changedPayload = new { productId, name = "Keyboard", unitPrice = 40m, currency = "EUR", quantity = 2, version = 0 };
        var reused = await Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, "stable-key", changedPayload));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Contains("idempotency_key_reused", await reused.Content.ReadAsStringAsync());

        var otherOperation = await Client.SendAsync(Request(HttpMethod.Delete,
            $"{uri}?version=2", created.AccessToken, "stable-key", new { }));
        Assert.Equal(HttpStatusCode.Conflict, otherOperation.StatusCode);
    }

    [Fact]
    public async Task Simultaneous_identical_requests_apply_once()
    {
        var created = await CreateCart();
        var productId = Guid.NewGuid();
        var uri = $"/api/v1/carts/{created.Cart.Id}/items";
        var add = new { productId, name = "Mouse", unitPrice = 20m, currency = "EUR", quantity = 1, version = 0 };

        var responses = await Task.WhenAll(
            Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, "race-key", add)),
            Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, "race-key", add)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var carts = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<CartDto>()));
        Assert.All(carts, cart => Assert.Equal(1, Assert.Single(cart!.Items).Quantity));
    }

    [Fact]
    public async Task Competing_updates_using_separate_database_contexts_fail_optimistically()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var cart = new ShoppingCart(Guid.NewGuid(), "hash", now);
        var productId = Guid.NewGuid();
        cart.AddItem(productId, "Keyboard", new Money(10m, "EUR"), 1, now.AddMinutes(1));

        await using (var seedScope = _factory!.Services.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<CartDbContext>();
            seed.Carts.Add(cart);
            await seed.SaveChangesAsync();
        }

        await using var firstScope = _factory!.Services.CreateAsyncScope();
        await using var secondScope = _factory!.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var firstCart = await first.Carts.Include(x => x.Items).SingleAsync(x => x.Id == cart.Id);
        var secondCart = await second.Carts.Include(x => x.Items).SingleAsync(x => x.Id == cart.Id);

        firstCart.SetQuantity(productId, 2, now.AddMinutes(2));
        secondCart.SetQuantity(productId, 3, now.AddMinutes(3));

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Expired_key_can_be_used_for_a_new_request()
    {
        var created = await CreateCart();
        var uri = $"/api/v1/carts/{created.Cart.Id}/items";
        var first = new { productId = Guid.NewGuid(), name = "Mouse", unitPrice = 20m, currency = "EUR", quantity = 1, version = 0 };
        Assert.Equal(HttpStatusCode.OK,
            (await Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, "expiring-key", first))).StatusCode);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
            var record = await db.IdempotencyRecords.FindAsync(created.Cart.Id, "expiring-key");
            record!.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var second = new { productId = Guid.NewGuid(), name = "Light", unitPrice = 30m, currency = "EUR", quantity = 1, version = 1 };
        var response = await Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, "expiring-key", second));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<CartDto>())!.Items.Count);
    }

    [Fact]
    public async Task Invalid_contracts_return_validation_problem_details()
    {
        var created = await CreateCart();
        var uri = $"/api/v1/carts/{created.Cart.Id}/items";
        object[] invalidRequests =
        [
            new { productId = Guid.Empty, name = "Product", unitPrice = 1m, currency = "EUR", quantity = 1, version = 0 },
            new { productId = Guid.NewGuid(), name = " Product ", unitPrice = 1m, currency = "EUR", quantity = 1, version = 0 },
            new { productId = Guid.NewGuid(), name = "Product", unitPrice = 1.001m, currency = "EUR", quantity = 1, version = 0 },
            new { productId = Guid.NewGuid(), name = "Product", unitPrice = 1m, currency = "E1R", quantity = 1, version = 0 },
            new { productId = Guid.NewGuid(), name = "Product", unitPrice = -0.01m, currency = "EUR", quantity = 1, version = 0 },
            new { productId = Guid.NewGuid(), name = new string('x', 201), unitPrice = 1m, currency = "EUR", quantity = 1, version = 0 },
            new { productId = Guid.NewGuid(), name = "Product", unitPrice = 1m, currency = "EUR", quantity = 0, version = 0 },
            new { productId = Guid.NewGuid(), name = "Product", unitPrice = 1m, currency = "EUR", quantity = 1000, version = 0 },
            new { productId = Guid.NewGuid(), name = "Product", unitPrice = 1m, currency = "EUR", quantity = 1, version = -1 }
        ];

        foreach (var invalid in invalidRequests)
        {
            var response = await Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, Guid.NewGuid().ToString("N"), invalid));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertProblem(response, "validation_error", HttpStatusCode.BadRequest, expectErrors: true);
        }

        var valid = new { productId = Guid.NewGuid(), name = "Product", unitPrice = 1m, currency = "EUR", quantity = 1, version = 0 };
        var invalidKey = await Client.SendAsync(Request(HttpMethod.Post, uri, created.AccessToken, "invalid key", valid));
        Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
        await AssertProblem(invalidKey, "validation_error", HttpStatusCode.BadRequest, expectErrors: true);

        var missingKey = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(valid)
        };
        missingKey.Headers.Add("X-Cart-Token", created.AccessToken);
        var missingKeyResponse = await Client.SendAsync(missingKey);
        Assert.Equal(HttpStatusCode.BadRequest, missingKeyResponse.StatusCode);
        await AssertProblem(missingKeyResponse, "validation_error", HttpStatusCode.BadRequest, expectErrors: true);
    }

    [Fact]
    public async Task Missing_invalid_and_cross_cart_tokens_do_not_disclose_cart_existence()
    {
        var first = await CreateCart();
        var second = await CreateCart();

        var missing = await Client.GetAsync($"/api/v1/carts/{first.Cart.Id}");
        var invalid = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/carts/{first.Cart.Id}");
        invalid.Headers.Add("X-Cart-Token", "invalid");
        var crossCart = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/carts/{second.Cart.Id}");
        crossCart.Headers.Add("X-Cart-Token", first.AccessToken);
        var nonexistent = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/carts/{Guid.NewGuid()}");
        nonexistent.Headers.Add("X-Cart-Token", first.AccessToken);

        foreach (var response in new[] { missing, await Client.SendAsync(invalid), await Client.SendAsync(crossCart), await Client.SendAsync(nonexistent) })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertProblem(response, "cart_not_found", HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Cross_cart_token_cannot_mutate_another_cart()
    {
        var first = await CreateCart();
        var second = await CreateCart();

        var response = await Client.SendAsync(Request(HttpMethod.Post, $"/api/v1/carts/{second.Cart.Id}/items",
            first.AccessToken, "cross-cart-mutation",
            new { productId = Guid.NewGuid(), name = "Keyboard", unitPrice = 1m, currency = "EUR", quantity = 1, version = 0 }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblem(response, "cart_not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Domain_cart_size_limit_is_exposed_as_problem_details()
    {
        var created = await CreateCart();
        for (var index = 0; index < ShoppingCart.MaximumDistinctItems; index++)
        {
            await AddItem(created, Guid.NewGuid(), $"Product {index}", 1m, "EUR", 1, index, $"limit-{index}");
        }

        var response = await Client.SendAsync(Request(HttpMethod.Post, $"/api/v1/carts/{created.Cart.Id}/items",
            created.AccessToken, "limit-overflow",
            new { productId = Guid.NewGuid(), name = "Overflow", unitPrice = 1m, currency = "EUR", quantity = 1, version = ShoppingCart.MaximumDistinctItems }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblem(response, "cart_item_limit", HttpStatusCode.BadRequest);
    }

    private async Task<CreatedCart> CreateCart()
    {
        return await CreateCart(Client);
    }

    private static async Task<CreatedCart> CreateCart(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/carts", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedCart>())!;
    }

    private WebApplicationFactory<Program> FactoryWith(IReadOnlyDictionary<string, string?> overrides) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>(overrides)
            {
                ["ConnectionStrings:CartDatabase"] = _postgres.GetConnectionString(),
                ["ApplyMigrations"] = "true"
            };
            config.AddInMemoryCollection(values);
        }));

    private async Task<CartDto> AuthorizedGet(CreatedCart created)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/carts/{created.Cart.Id}");
        request.Headers.Add("X-Cart-Token", created.AccessToken);
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartDto>())!;
    }

    private async Task<CartDto> AddItem(CreatedCart created, Guid productId, string name, decimal unitPrice,
        string currency, int quantity, long version, string key) => await SendCart(Request(HttpMethod.Post,
        $"/api/v1/carts/{created.Cart.Id}/items", created.AccessToken, key,
        new { productId, name, unitPrice, currency, quantity, version }));

    private async Task<CartDto> SendCart(HttpRequestMessage request)
    {
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartDto>())!;
    }

    private static async Task AssertProblem(HttpResponseMessage response, string code, HttpStatusCode status,
        bool expectErrors = false)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal((int)status, root.GetProperty("status").GetInt32());
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        if (expectErrors) Assert.Equal(JsonValueKind.Object, root.GetProperty("errors").ValueKind);
    }

    private static void AssertCart(CartDto expected, CartDto actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Subtotal, actual.Subtotal);
        Assert.Equal(expected.Currency, actual.Currency);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal(expected.Items.OrderBy(x => x.ProductId).ToArray(), actual.Items.OrderBy(x => x.ProductId).ToArray());
    }

    private static HttpRequestMessage Request(HttpMethod method, string uri, string token, string key, object body)
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Cart-Token", token);
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }
}
