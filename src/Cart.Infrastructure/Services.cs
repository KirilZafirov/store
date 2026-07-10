using System.Security.Cryptography;
using System.Text;
using Cart.Application;

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
