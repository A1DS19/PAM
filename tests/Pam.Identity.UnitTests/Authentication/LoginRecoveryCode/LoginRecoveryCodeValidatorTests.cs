using FluentAssertions;
using Pam.Identity.Authentication.LoginRecoveryCode;
using Xunit;

namespace Pam.Identity.UnitTests.Authentication.LoginRecoveryCode;

public class LoginRecoveryCodeValidatorTests
{
    private readonly LoginRecoveryCodeValidator _validator = new();

    [Theory]
    [InlineData("abcde-fghij")]
    [InlineData("abcdefghij")]
    public void Valid_codes_pass(string code)
    {
        _validator.Validate(new LoginRecoveryCodeCommand(code)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_fails()
    {
        _validator.Validate(new LoginRecoveryCodeCommand("")).IsValid.Should().BeFalse();
    }
}
