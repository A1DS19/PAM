using FluentAssertions;
using Pam.Players.Players.Models;
using Xunit;

namespace Pam.Players.UnitTests;

public sealed class PlayerTests
{
    [Fact]
    public void Create_Stamps_BrandId_And_Email()
    {
        var brandId = Guid.CreateVersion7();

        var player = Player.Create(brandId, "test@example.com");

        player.BrandId.Should().Be(brandId);
        player.Email.Should().Be("test@example.com");
        player.Id.Should().NotBe(Guid.Empty);
    }
}
