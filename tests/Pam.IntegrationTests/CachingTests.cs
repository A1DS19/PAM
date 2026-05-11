using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pam.Shared.Contracts.Caching;
using StackExchange.Redis;
using Xunit;

namespace Pam.IntegrationTests;

[Collection(nameof(PamContainersCollection))]
public sealed class CachingTests(PamContainersFixture containers)
{
    private const string CacheKeyPrefix = "pam:cache:";

    public sealed record Payload(Guid Id, string Name);

    [Fact]
    public async Task SetAsync_then_GetAsync_round_trips_the_value()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        var cache = factory.Services.GetRequiredService<ICacheService>();
        var key = $"int-tests:roundtrip:{Guid.NewGuid():N}";
        var payload = new Payload(Guid.NewGuid(), "BetAnything EU");

        await cache.SetAsync(key, payload, TimeSpan.FromMinutes(5), ct);
        var got = await cache.GetAsync<Payload>(key, ct);

        got.Should().Be(payload);
    }

    [Fact]
    public async Task RemoveAsync_deletes_the_key()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        var cache = factory.Services.GetRequiredService<ICacheService>();
        var key = $"int-tests:remove:{Guid.NewGuid():N}";
        await cache.SetAsync(key, new Payload(Guid.Empty, "x"), TimeSpan.FromMinutes(1), ct);

        await cache.RemoveAsync(key, ct);
        var got = await cache.GetAsync<Payload>(key, ct);

        got.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_reflects_presence()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        var cache = factory.Services.GetRequiredService<ICacheService>();
        var key = $"int-tests:exists:{Guid.NewGuid():N}";

        (await cache.ExistsAsync(key, ct)).Should().BeFalse();
        await cache.SetAsync(key, new Payload(Guid.Empty, "x"), TimeSpan.FromMinutes(1), ct);
        (await cache.ExistsAsync(key, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveByPatternAsync_only_deletes_matching_keys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        var cache = factory.Services.GetRequiredService<ICacheService>();
        var run = Guid.NewGuid().ToString("N");

        var matching1 = $"int-tests:{run}:brand:a";
        var matching2 = $"int-tests:{run}:brand:b";
        var unrelated = $"int-tests:{run}:player:c";

        await cache.SetAsync(matching1, new Payload(Guid.Empty, "a"), TimeSpan.FromMinutes(5), ct);
        await cache.SetAsync(matching2, new Payload(Guid.Empty, "b"), TimeSpan.FromMinutes(5), ct);
        await cache.SetAsync(unrelated, new Payload(Guid.Empty, "c"), TimeSpan.FromMinutes(5), ct);

        await cache.RemoveByPatternAsync($"int-tests:{run}:brand:*", ct);

        (await cache.ExistsAsync(matching1, ct)).Should().BeFalse();
        (await cache.ExistsAsync(matching2, ct)).Should().BeFalse();
        (await cache.ExistsAsync(unrelated, ct)).Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_writes_under_the_pam_cache_prefix()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        var cache = factory.Services.GetRequiredService<ICacheService>();
        var multiplexer = factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var key = $"int-tests:prefix:{Guid.NewGuid():N}";

        await cache.SetAsync(key, new Payload(Guid.Empty, "x"), TimeSpan.FromMinutes(1), ct);

        var db = multiplexer.GetDatabase();
        (await db.KeyExistsAsync(CacheKeyPrefix + key)).Should().BeTrue();
        (await db.KeyExistsAsync(key)).Should().BeFalse();
    }
}
