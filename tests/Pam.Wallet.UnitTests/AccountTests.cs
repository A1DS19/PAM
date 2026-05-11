using FluentAssertions;
using Pam.Wallet.Accounts.Models;
using Xunit;

namespace Pam.Wallet.UnitTests;

public sealed class AccountTests
{
    [Fact]
    public void Open_Stamps_Brand_Player_And_Currency()
    {
        var brandId = Guid.CreateVersion7();
        var playerId = Guid.CreateVersion7();

        var account = Account.Open(brandId, playerId, "USD");

        account.BrandId.Should().Be(brandId);
        account.PlayerId.Should().Be(playerId);
        account.Currency.Should().Be("USD");
        account.Id.Should().NotBe(Guid.Empty);
    }
}
