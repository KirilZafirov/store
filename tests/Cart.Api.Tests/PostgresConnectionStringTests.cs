using Cart.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Cart.Api.Tests;

public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void Neon_uri_without_explicit_port_uses_postgresql_default()
    {
        var normalized = DependencyInjection.NormalizePostgresConnectionString(
            "postgresql://cart_owner:p%40ss@ep-example.eu-central-1.aws.neon.tech/cart?sslmode=require");
        var builder = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal(5432, builder.Port);
        Assert.Equal("p@ss", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void Uri_parser_preserves_escaped_credentials_database_port_and_supported_parameters()
    {
        var normalized = DependencyInjection.NormalizePostgresConnectionString(
            "postgres://cart%2Bapp:p%40ss%3Bword@db.example.test:6543/cart%2Fblue?sslmode=verify-full&application_name=atlas&connect_timeout=7&command_timeout=11&max_pool_size=12");
        var builder = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("db.example.test", builder.Host);
        Assert.Equal(6543, builder.Port);
        Assert.Equal("cart/blue", builder.Database);
        Assert.Equal("cart+app", builder.Username);
        Assert.Equal("p@ss;word", builder.Password);
        Assert.Equal(SslMode.VerifyFull, builder.SslMode);
        Assert.Equal("atlas", builder.ApplicationName);
        Assert.Equal(7, builder.Timeout);
        Assert.Equal(11, builder.CommandTimeout);
        Assert.Equal(12, builder.MaxPoolSize);
    }

    [Fact]
    public void Configuration_overrides_apply_bounded_pool_and_timeout_defaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MaxPoolSize"] = "25",
            ["Database:ConnectionTimeoutSeconds"] = "6",
            ["Database:CommandTimeoutSeconds"] = "9"
        }).Build();

        var normalized = DependencyInjection.BuildPostgresConnectionString(
            "Host=localhost;Database=cart;Username=cart;Password=secret", configuration);
        var builder = new NpgsqlConnectionStringBuilder(normalized);

        Assert.True(builder.Pooling);
        Assert.Equal(25, builder.MaxPoolSize);
        Assert.Equal(6, builder.Timeout);
        Assert.Equal(9, builder.CommandTimeout);
    }
}
