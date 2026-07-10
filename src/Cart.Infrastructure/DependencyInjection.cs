using Cart.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CartDbContext>((provider, options) =>
        {
            var runtimeConfiguration = provider.GetRequiredService<IConfiguration>();
            var databaseConnection = BuildPostgresConnectionString(
                runtimeConfiguration.GetConnectionString("CartDatabase")
                ?? throw new InvalidOperationException("ConnectionStrings:CartDatabase is required."),
                runtimeConfiguration);
            options.UseNpgsql(databaseConnection, npgsql =>
            {
                npgsql.EnableRetryOnFailure();
                npgsql.CommandTimeout(IntSetting(runtimeConfiguration, "Database:CommandTimeoutSeconds", 30));
            });
        });
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }

    public static string NormalizePostgresConnectionString(string value) => BuildPostgresConnectionString(value, null);

    public static string BuildPostgresConnectionString(string value, IConfiguration? configuration)
    {
        var builder = Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == "postgres" || uri.Scheme == "postgresql")
            ? FromPostgresUri(uri)
            : new NpgsqlConnectionStringBuilder(value);

        builder.Pooling = true;
        builder.MaxPoolSize = IntSetting(configuration, "Database:MaxPoolSize", builder.MaxPoolSize > 0 ? builder.MaxPoolSize : 50);
        builder.Timeout = IntSetting(configuration, "Database:ConnectionTimeoutSeconds", builder.Timeout > 0 ? builder.Timeout : 15);
        builder.CommandTimeout = IntSetting(configuration, "Database:CommandTimeoutSeconds", builder.CommandTimeout > 0 ? builder.CommandTimeout : 30);
        return builder.ConnectionString;
    }

    private static int IntSetting(IConfiguration? configuration, string key, int fallback) =>
        int.TryParse(configuration?[key], System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static NpgsqlConnectionStringBuilder FromPostgresUri(Uri uri)
    {
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length == 2 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            SslMode = SslMode.Require
        };

        foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = parameter.Split('=', 2);
            var name = Uri.UnescapeDataString(parts[0]).Replace("_", " ", StringComparison.Ordinal).ToLowerInvariant();
            var parameterValue = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (name is "sslmode" or "ssl mode")
                builder.SslMode = Enum.Parse<SslMode>(parameterValue.Replace("-", "", StringComparison.Ordinal), ignoreCase: true);
            else if (name is "application name" or "application_name")
                builder.ApplicationName = parameterValue;
            else if (name is "connect timeout" or "connect_timeout" or "timeout")
                builder.Timeout = int.Parse(parameterValue, System.Globalization.CultureInfo.InvariantCulture);
            else if (name is "command timeout" or "command_timeout")
                builder.CommandTimeout = int.Parse(parameterValue, System.Globalization.CultureInfo.InvariantCulture);
            else if (name is "pooling")
                builder.Pooling = bool.Parse(parameterValue);
            else if (name is "max pool size" or "max_pool_size")
                builder.MaxPoolSize = int.Parse(parameterValue, System.Globalization.CultureInfo.InvariantCulture);
        }

        return builder;
    }
}
