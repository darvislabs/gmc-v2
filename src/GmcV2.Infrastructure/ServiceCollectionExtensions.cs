using GmcV2.Core;
using Microsoft.Extensions.DependencyInjection;

namespace GmcV2.Infrastructure;

public static class GmcV2Infrastructure
{
    /// <summary>
    /// Registers the persistence boundary. dbMode:
    ///   "memory" → InMemoryRepository singleton (dev/CI default, no SQL needed);
    ///   "sql"    → SqlRepository singleton bound to sqlConnectionString.
    /// </summary>
    public static IServiceCollection AddGmcV2Infrastructure(this IServiceCollection services, string dbMode, string? sqlConnectionString)
    {
        var mode = (dbMode ?? "memory").Trim().ToLowerInvariant();
        if (mode == "sql")
        {
            var cs = sqlConnectionString ?? Environment.GetEnvironmentVariable("GMCV2_SQL");
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("dbMode 'sql' requires a connection string (argument or env GMCV2_SQL).");
            services.AddSingleton<IGmcRepository>(_ => new SqlRepository(cs));
        }
        else
        {
            services.AddSingleton<IGmcRepository>(_ => new InMemoryRepository());
        }
        return services;
    }
}
