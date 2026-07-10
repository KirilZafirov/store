using System.Net;
using System.Net.Http.Json;
using Cart.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

    private static HttpRequestMessage Request(HttpMethod method, string uri, string token, string key, object body)
    {
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Cart-Token", token);
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }
}
