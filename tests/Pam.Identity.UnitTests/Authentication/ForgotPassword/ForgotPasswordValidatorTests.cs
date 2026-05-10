using FluentAssertions;
using Pam.Identity.Authentication.ForgotPassword;
using Xunit;

namespace Pam.Identity.UnitTests.Authentication.ForgotPassword;

public class ForgotPasswordValidatorTests
{
    private readonly ForgotPasswordValidator _validator = new();

    [Fact]
    public void Valid_email_passes()
    {
        _validator.Validate(new ForgotPasswordCommand("owner@example.com"))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    public void Invalid_email_fails(string email)
    {
        _validator.Validate(new ForgotPasswordCommand(email)).IsValid.Should().BeFalse();
    }
}
