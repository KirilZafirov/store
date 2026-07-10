using Cart.Infrastructure;
using Xunit;

namespace Cart.Api.Tests;

public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void Neon_uri_without_explicit_port_uses_postgresql_default()
    {
        var normalized = DependencyInjection.NormalizePostgresConnectionString(
            "postgresql://cart_owner:p%40ss@ep-example.eu-central-1.aws.neon.tech/cart?sslmode=require");

        Assert.Contains("Port=5432", normalized);
        Assert.Contains("Password=p@ss", normalized);
        Assert.Contains("SSL Mode=Require", normalized);
    }
}
