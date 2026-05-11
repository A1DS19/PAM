using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pam.Shared.Behaviors;
using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;
using Xunit;

namespace Pam.Shared.UnitTests.Caching;

public sealed class CachingBehaviorTests
{
    public sealed record BrandDto(Guid Id, string Name);

    public sealed record GetBrandWithoutCacheAttrQuery(Guid Id) : IQuery<BrandDto>;

    [Cache(durationMinutes: 10, keyPattern: "operators:brand:{Id}")]
    public sealed record GetBrandQuery(Guid Id) : IQuery<BrandDto>;

    [InvalidateCache("operators:brand:*", "operators:list:*")]
    public sealed record UpdateBrandCommand(Guid Id, string Name) : ICommand<BrandDto>;

    public sealed record PlainCommand(Guid Id) : ICommand<BrandDto>;

    [Fact]
    public async Task Query_without_cache_attribute_bypasses_the_cache()
    {
        var cache = Substitute.For<ICacheService>();
        var sut = CreateSut<GetBrandWithoutCacheAttrQuery, BrandDto>(cache);
        var expected = new BrandDto(Guid.NewGuid(), "BetAnything EU");
        var handlerCalls = 0;

        var actual = await sut.Handle(
            new GetBrandWithoutCacheAttrQuery(Guid.NewGuid()),
            () =>
            {
                handlerCalls++;
                return Task.FromResult(expected);
            },
            CancellationToken.None
        );

        actual.Should().Be(expected);
        handlerCalls.Should().Be(1);
        await cache
            .DidNotReceive()
            .GetAsync<BrandDto>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cache
            .DidNotReceive()
            .SetAsync(
                Arg.Any<string>(),
                Arg.Any<BrandDto>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Query_with_cache_miss_calls_handler_then_caches_response()
    {
        var cache = Substitute.For<ICacheService>();
        cache
            .GetAsync<BrandDto>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BrandDto?)null);

        var sut = CreateSut<GetBrandQuery, BrandDto>(cache);
        var brandId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var expected = new BrandDto(brandId, "BetAnything EU");

        var actual = await sut.Handle(
            new GetBrandQuery(brandId),
            () => Task.FromResult(expected),
            CancellationToken.None
        );

        actual.Should().Be(expected);
        await cache
            .Received(1)
            .GetAsync<BrandDto>(
                "operators:brand:11111111-1111-1111-1111-111111111111",
                Arg.Any<CancellationToken>()
            );
        await cache
            .Received(1)
            .SetAsync(
                "operators:brand:11111111-1111-1111-1111-111111111111",
                expected,
                TimeSpan.FromMinutes(10),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Query_with_cache_hit_returns_cached_value_without_invoking_handler()
    {
        var cache = Substitute.For<ICacheService>();
        var cached = new BrandDto(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Cached");
        cache.GetAsync<BrandDto>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(cached);

        var sut = CreateSut<GetBrandQuery, BrandDto>(cache);
        var handlerCalls = 0;

        var actual = await sut.Handle(
            new GetBrandQuery(cached.Id),
            () =>
            {
                handlerCalls++;
                return Task.FromResult(new BrandDto(cached.Id, "Fresh"));
            },
            CancellationToken.None
        );

        actual.Should().Be(cached);
        handlerCalls.Should().Be(0);
        await cache
            .DidNotReceive()
            .SetAsync(
                Arg.Any<string>(),
                Arg.Any<BrandDto>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Command_with_invalidate_attribute_invalidates_each_pattern_after_handler_succeeds()
    {
        var cache = Substitute.For<ICacheService>();
        var sut = CreateSut<UpdateBrandCommand, BrandDto>(cache);
        var response = new BrandDto(Guid.NewGuid(), "Updated");

        var actual = await sut.Handle(
            new UpdateBrandCommand(response.Id, "Updated"),
            () => Task.FromResult(response),
            CancellationToken.None
        );

        actual.Should().Be(response);
        await cache
            .Received(1)
            .RemoveByPatternAsync("operators:brand:*", Arg.Any<CancellationToken>());
        await cache
            .Received(1)
            .RemoveByPatternAsync("operators:list:*", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Command_without_invalidate_attribute_does_not_touch_the_cache()
    {
        var cache = Substitute.For<ICacheService>();
        var sut = CreateSut<PlainCommand, BrandDto>(cache);
        var response = new BrandDto(Guid.NewGuid(), "Unchanged");

        var actual = await sut.Handle(
            new PlainCommand(response.Id),
            () => Task.FromResult(response),
            CancellationToken.None
        );

        actual.Should().Be(response);
        await cache
            .DidNotReceive()
            .RemoveByPatternAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Command_invalidation_failure_does_not_propagate()
    {
        var cache = Substitute.For<ICacheService>();
        cache
            .RemoveByPatternAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("redis down")));

        var sut = CreateSut<UpdateBrandCommand, BrandDto>(cache);
        var response = new BrandDto(Guid.NewGuid(), "Updated");

        var actual = await sut.Handle(
            new UpdateBrandCommand(response.Id, "Updated"),
            () => Task.FromResult(response),
            CancellationToken.None
        );

        actual.Should().Be(response);
    }

    private static CachingBehavior<TRequest, TResponse> CreateSut<TRequest, TResponse>(
        ICacheService cache
    )
        where TRequest : notnull
        where TResponse : notnull =>
        new(cache, NullLogger<CachingBehavior<TRequest, TResponse>>.Instance);
}
