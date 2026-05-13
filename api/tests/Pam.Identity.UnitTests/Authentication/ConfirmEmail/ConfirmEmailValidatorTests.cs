using FluentAssertions;
using Pam.Identity.Authentication.ConfirmEmail;
using Xunit;

namespace Pam.Identity.UnitTests.Authentication.ConfirmEmail;

public class ConfirmEmailValidatorTests
{
    private readonly ConfirmEmailValidator _validator = new();

    [Fact]
    public void Valid_command_passes()
    {
        _validator.Validate(new ConfirmEmailCommand("owner@example.com", "token"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_email_fails()
    {
        _validator.Validate(new ConfirmEmailCommand("", "token")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_token_fails()
    {
        _validator.Validate(new ConfirmEmailCommand("owner@example.com", "")).IsValid.Should().BeFalse();
    }
}
