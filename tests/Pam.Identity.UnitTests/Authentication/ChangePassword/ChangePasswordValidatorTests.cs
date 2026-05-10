using FluentAssertions;
using Pam.Identity.Authentication.ChangePassword;
using Xunit;

namespace Pam.Identity.UnitTests.Authentication.ChangePassword;

public class ChangePasswordValidatorTests
{
    private readonly ChangePasswordValidator _validator = new();

    [Fact]
    public void Valid_change_passes()
    {
        var command = new ChangePasswordCommand("OldPassword123!", "NewPassword456!");

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Same_new_and_current_fails()
    {
        var command = new ChangePasswordCommand("SamePassword123!", "SamePassword123!");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should()
            .Contain(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("eleven-cha")]
    public void New_password_under_12_fails(string password)
    {
        var command = new ChangePasswordCommand("OldPassword123!", password);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_current_fails()
    {
        var command = new ChangePasswordCommand(string.Empty, "NewPassword456!");

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
