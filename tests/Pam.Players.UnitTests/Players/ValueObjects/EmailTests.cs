using FluentAssertions;
using Pam.Players.Players.ValueObjects;
using Xunit;

namespace Pam.Players.UnitTests.Players.ValueObjects;

public class EmailTests
{
    [Theory]
    [InlineData("alice@example.com", "alice@example.com")]
    [InlineData("ALICE@example.com", "alice@example.com")]
    [InlineData("  alice@example.com  ", "alice@example.com")]
    [InlineData("Alice+tag@Example.COM", "alice+tag@example.com")]
    public void Create_normalizes_to_lowercase_and_trims(string input, string expected)
    {
        var email = Email.Create(input);
        email.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_rejects_null_or_whitespace(string raw)
    {
        var act = () => Email.Create(raw);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_null()
    {
        var act = () => Email.Create(null!);
        act.Should().Throw<ArgumentException>();
    }
}
