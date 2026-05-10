using FluentAssertions;
using Pam.Identity.Authentication.Mfa;
using Xunit;

namespace Pam.Identity.UnitTests.Authentication.Mfa;

public class MfaVerifyValidatorTests
{
    private readonly MfaVerifyValidator _validator = new();

    [Theory]
    [InlineData("123456")]
    [InlineData("12345678")]
    public void Valid_code_lengths_pass(string code)
    {
        _validator.Validate(new MfaVerifyCommand(code)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("123456789")]
    public void Invalid_code_lengths_fail(string code)
    {
        _validator.Validate(new MfaVerifyCommand(code)).IsValid.Should().BeFalse();
    }
}
