using System.Text.Json;
using OrderFlow.Contracts.Abstractions;
using StackExchange.Redis;

namespace OrderFlow.Infrastructure.Caching;

/// <summary>
/// Production <see cref="ICacheService"/> backed by Redis via StackExchange.Redis.
/// Drop-in replacement for the dev DistributedCacheService — same JSON contract,
/// same keys (see <see cref="CacheKeys"/>), so no caller changes when switching
/// from in-memory to Redis. Used for catalog reads and order-item lookups in the
/// saga, which is what keeps transaction routing fast under peak load.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer _redis;

    public RedisCacheService(IConnectionMultiplexer redis) => _redis = redis;

    private IDatabase Db => _redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        RedisValue value = await Db.StringGetAsync(key);
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>(value!, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await Db.StringSetAsync(key, json, ttl ?? TimeSpan.FromMinutes(5));
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) => Db.KeyDeleteAsync(key);
}
