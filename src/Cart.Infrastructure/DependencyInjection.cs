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
        services.AddDbContext<CartDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("CartDatabase"), npgsql => npgsql.EnableRetryOnFailure()));
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
}
