using FluentAssertions;
using Pam.Identity.Contracts.Roles;
using Pam.Identity.Users.Features.CreateUser;
using Xunit;

namespace Pam.Identity.UnitTests.Users.Features.CreateUser;

public class CreateUserValidatorTests
{
    private readonly CreateUserValidator _validator = new();

    [Fact]
    public void Valid_command_passes()
    {
        var command = new CreateUserCommand(
            "owner@example.com",
            "CorrectHorseBatteryStaple1!",
            BrandId: null,
            Roles: [RoleNames.Manager]
        );

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    public void Invalid_email_fails(string email)
    {
        var command = new CreateUserCommand(
            email,
            "CorrectHorseBatteryStaple1!",
            BrandId: null,
            Roles: []
        );

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateUserCommand.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("eleven-cha")]
    public void Password_under_12_chars_fails(string password)
    {
        var command = new CreateUserCommand(
            "owner@example.com",
            password,
            BrandId: null,
            Roles: []
        );

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Unknown_role_fails()
    {
        var command = new CreateUserCommand(
            "owner@example.com",
            "CorrectHorseBatteryStaple1!",
            BrandId: null,
            Roles: ["MadeUpRole"]
        );

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("Roles", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_roles_passes()
    {
        var command = new CreateUserCommand(
            "owner@example.com",
            "CorrectHorseBatteryStaple1!",
            BrandId: null,
            Roles: []
        );

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void All_seeded_roles_are_accepted()
    {
        var command = new CreateUserCommand(
            "owner@example.com",
            "CorrectHorseBatteryStaple1!",
            BrandId: null,
            Roles: [.. RoleNames.All]
        );

        _validator.Validate(command).IsValid.Should().BeTrue();
    }
}
