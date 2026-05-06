using FluentAssertions;
using Pam.Players.Players.ValueObjects;
using Xunit;

namespace Pam.Players.UnitTests.Players.ValueObjects;

public class PersonalNameTests
{
    [Fact]
    public void Display_omits_middle_when_absent()
    {
        new PersonalName("Alice", "Tester").Display.Should().Be("Alice Tester");
    }

    [Fact]
    public void Display_includes_middle_when_present()
    {
        new PersonalName("Alice", "Tester", "Q").Display.Should().Be("Alice Q Tester");
    }
}
