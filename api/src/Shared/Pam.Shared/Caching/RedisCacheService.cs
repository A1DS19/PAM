using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pam.Shared.Contracts.Caching;
using StackExchange.Redis;

namespace Pam.Shared.Caching;

public sealed class RedisCacheService : ICacheService
{
    // Namespacing every cache entry under "pam:cache:" keeps this from
    // colliding with the rate-limiter ("pam-api-rl:*") and leaves room
    // for future Redis uses (locks, sessions) without `KEYS *` surprises.
    public const string KeyPrefix = "pam:cache:";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer multiplexer, ILogger<RedisCacheService> logger)
    {
        _multiplexer = multiplexer;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _multiplexer.GetDatabase().StringGetAsync(Prefix(key));
            if (value.IsNullOrEmpty)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>((string)value!, SerializerOptions);
        }
        catch (Exception ex)
        {
            // A Redis blip on a read must not break the query path — the
            // caller will fall through to the handler and refresh on Set.
            _logger.LogWarning(ex, "Redis cache GET failed for key {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var payload = JsonSerializer.Serialize(value, SerializerOptions);
            var database = _multiplexer.GetDatabase();
            if (expiration.HasValue)
            {
                await database.StringSetAsync(Prefix(key), payload, expiration.Value);
            }
            else
            {
                await database.StringSetAsync(Prefix(key), payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache SET failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _multiplexer.GetDatabase().KeyDeleteAsync(Prefix(key));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache DEL failed for key {Key}", key);
        }
    }

    public async Task RemoveByPatternAsync(
        string pattern,
        CancellationToken cancellationToken = default
    )
    {
        var prefixed = Prefix(pattern);
        var database = _multiplexer.GetDatabase();

        foreach (var endpoint in _multiplexer.GetEndPoints())
        {
            var server = _multiplexer.GetServer(endpoint);
            if (server.IsReplica || !server.IsConnected)
            {
                continue;
            }

            try
            {
                await foreach (
                    var key in server
                        .KeysAsync(pattern: prefixed)
                        .WithCancellation(cancellationToken)
                )
                {
                    await database.KeyDeleteAsync(key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Redis cache pattern delete failed for pattern {Pattern} on {Endpoint}",
                    pattern,
                    endpoint
                );
            }
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _multiplexer.GetDatabase().KeyExistsAsync(Prefix(key));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache EXISTS failed for key {Key}", key);
            return false;
        }
    }

    private static string Prefix(string key) =>
        key.StartsWith(KeyPrefix, StringComparison.Ordinal) ? key : KeyPrefix + key;
}
