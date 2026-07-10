using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cart.Application;
using Cart.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
                ["ConnectionStrings:Redis"] = "",
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

        var forbidden = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/carts/{created.Cart.Id}");
        forbidden.Headers.Add("X-Cart-Token", new string('0', 64));
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.SendAsync(forbidden)).StatusCode);
    }

    [Fact]
    public async Task Health_endpoints_report_liveness_and_database_readiness()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/health/ready")).StatusCode);
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
        Assert.Equal(original, replayed);

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
            new { productId = Guid.NewGuid(), name = "Product", unitPrice = 1m, currency = "EUR", quantity = 0, version = 0 },
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

    private async Task<CreatedCart> CreateCart()
    {
        var response = await Client.PostAsync("/api/v1/carts", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedCart>())!;
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

    private static HttpRequestMessage Request(HttpMethod method, string uri, string token, string key, object body)
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Cart-Token", token);
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }
}
