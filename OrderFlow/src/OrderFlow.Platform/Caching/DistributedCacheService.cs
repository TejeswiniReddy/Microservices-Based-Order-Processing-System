using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderFlow.Contracts.Abstractions;

namespace OrderFlow.Platform.Caching;

/// <summary>
/// <see cref="ICacheService"/> backed by <see cref="IDistributedCache"/> with
/// JSON serialization. In dev/tests this uses the in-memory distributed cache;
/// the production RedisCacheService (OrderFlow.Infrastructure) implements the
/// exact same contract against StackExchange.Redis, so callers never change.
/// </summary>
public sealed class DistributedCacheService : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDistributedCache _cache;

    public DistributedCacheService(IDistributedCache cache) => _cache = cache;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var bytes = await _cache.GetAsync(key, ct);
        return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        where T : class
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromMinutes(5)
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await _cache.SetAsync(key, bytes, options, ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) => _cache.RemoveAsync(key, ct);
}

public static class CachingRegistration
{
    /// <summary>
    /// Registers the in-memory distributed cache. Production (EnableCloud=true)
    /// swaps the ICacheService for the Redis-backed implementation.
    /// </summary>
    public static IServiceCollection AddInMemoryCache(this IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
        services.TryAddSingleton<ICacheService, DistributedCacheService>();
        return services;
    }
}
