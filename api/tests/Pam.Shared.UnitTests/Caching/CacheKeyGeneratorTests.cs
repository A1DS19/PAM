using FluentAssertions;
using Pam.Shared.Contracts.Caching;
using Xunit;

namespace Pam.Shared.UnitTests.Caching;

public sealed class CacheKeyGeneratorTests
{
    private sealed record SampleQuery(Guid BrandId, string Name);

    [Fact]
    public void GenerateKey_with_pattern_interpolates_property_values()
    {
        var query = new SampleQuery(Guid.Parse("11111111-1111-1111-1111-111111111111"), "EU");

        var key = CacheKeyGenerator.GenerateKey(query, "operators:brand:{BrandId}:{Name}");

        key.Should().Be("operators:brand:11111111-1111-1111-1111-111111111111:EU");
    }

    [Fact]
    public void GenerateKey_without_pattern_hashes_the_payload_and_prefixes_with_type_name()
    {
        var query = new SampleQuery(Guid.Empty, "EU");

        var key = CacheKeyGenerator.GenerateKey(query);

        key.Should().StartWith("SampleQuery:");
        key.Length.Should().Be("SampleQuery:".Length + 16);
    }

    [Fact]
    public void GenerateKey_pattern_substitutes_null_properties_as_string_null()
    {
        var query = new SampleQuery(Guid.Empty, null!);

        var key = CacheKeyGenerator.GenerateKey(query, "x:{Name}");

        key.Should().Be("x:null");
    }

    [Fact]
    public void GenerateKey_pattern_match_is_case_insensitive()
    {
        var query = new SampleQuery(Guid.Parse("11111111-1111-1111-1111-111111111111"), "EU");

        var key = CacheKeyGenerator.GenerateKey(query, "x:{brandid}");

        key.Should().Be("x:11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void GenerateKey_is_stable_for_equal_requests()
    {
        var q1 = new SampleQuery(Guid.Parse("22222222-2222-2222-2222-222222222222"), "US");
        var q2 = new SampleQuery(Guid.Parse("22222222-2222-2222-2222-222222222222"), "US");

        CacheKeyGenerator.GenerateKey(q1).Should().Be(CacheKeyGenerator.GenerateKey(q2));
    }

    [Fact]
    public void GenerateKey_differs_for_distinct_requests()
    {
        var q1 = new SampleQuery(Guid.Parse("22222222-2222-2222-2222-222222222222"), "US");
        var q2 = new SampleQuery(Guid.Parse("33333333-3333-3333-3333-333333333333"), "US");

        CacheKeyGenerator.GenerateKey(q1).Should().NotBe(CacheKeyGenerator.GenerateKey(q2));
    }
}
