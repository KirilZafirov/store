using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cart.Application;
using StackExchange.Redis;

namespace Cart.Infrastructure;

public sealed class TokenService : ITokenService
{
    public string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}

public sealed class RedisCartCache(IConnectionMultiplexer? redis) : ICartCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CartDto?> Get(Guid id, CancellationToken ct)
    {
        if (redis is null || !redis.IsConnected) return null;
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(id));
            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CartDto>((string)value!, JsonOptions);
        }
        catch (RedisException) { return null; }
    }

    public async Task Set(CartDto cart, CancellationToken ct)
    {
        if (redis is null || !redis.IsConnected) return;
        try { await redis.GetDatabase().StringSetAsync(Key(cart.Id), JsonSerializer.Serialize(cart, JsonOptions), TimeSpan.FromMinutes(5)); }
        catch (RedisException) { }
    }

    public async Task Remove(Guid id, CancellationToken ct)
    {
        if (redis is null || !redis.IsConnected) return;
        try { await redis.GetDatabase().KeyDeleteAsync(Key(id)); }
        catch (RedisException) { }
    }

    private static string Key(Guid id) => $"cart:{id:N}";
}
