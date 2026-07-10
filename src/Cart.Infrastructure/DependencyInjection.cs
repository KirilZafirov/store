using Cart.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Cart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseConnection = NormalizePostgresConnectionString(
            configuration.GetConnectionString("CartDatabase")
            ?? throw new InvalidOperationException("ConnectionStrings:CartDatabase is required."));
        services.AddDbContext<CartDbContext>(o =>
            o.UseNpgsql(databaseConnection, npgsql => npgsql.EnableRetryOnFailure()));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IClock, SystemClock>();

        var redisConnection = configuration.GetConnectionString("Redis");
        services.AddSingleton<ICartCache>(_ => new RedisCartCache(string.IsNullOrWhiteSpace(redisConnection)
            ? null
            : ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { redisConnection }, AbortOnConnectFail = false, ConnectTimeout = 500
            })));
        return services;
    }

    private static string NormalizePostgresConnectionString(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql")) return value;

        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length == 2 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        return $"Host={uri.Host};Port={uri.Port};Database={database};Username={username};Password={password};SSL Mode=Require";
    }
}
