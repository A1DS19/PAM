using FluentAssertions;
using Pam.Identity.Authentication.ResetPassword;
using Xunit;

namespace Pam.Identity.UnitTests.Authentication.ResetPassword;

public class ResetPasswordValidatorTests
{
    private readonly ResetPasswordValidator _validator = new();

    [Fact]
    public void Valid_command_passes()
    {
        var command = new ResetPasswordCommand(
            "owner@example.com",
            Token: new string('a', 64),
            NewPassword: "CorrectHorseBatteryStaple1!"
        );

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Invalid_email_fails(string email)
    {
        var command = new ResetPasswordCommand(email, "token", "CorrectHorseBatteryStaple1!");
        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("eleven-cha")]
    public void Weak_new_password_fails(string password)
    {
        var command = new ResetPasswordCommand("owner@example.com", "token", password);
        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_token_fails()
    {
        var command = new ResetPasswordCommand("owner@example.com", "", "CorrectHorseBatteryStaple1!");
        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
