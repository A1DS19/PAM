using FluentAssertions;
using Pam.Players.Players.ValueObjects;
using Xunit;

namespace Pam.Players.UnitTests.Players.ValueObjects;

public class JurisdictionTests
{
    [Fact]
    public void ToString_renders_country_alone_when_region_absent()
    {
        new Jurisdiction("US").ToString().Should().Be("US");
    }

    [Fact]
    public void ToString_renders_country_dash_region_when_present()
    {
        new Jurisdiction("US", "NY").ToString().Should().Be("US-NY");
    }

    [Fact]
    public void Records_with_same_values_are_equal()
    {
        new Jurisdiction("US", "NY").Should().Be(new Jurisdiction("US", "NY"));
    }
}
