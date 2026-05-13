using FluentAssertions;
using Pam.Operators.Brands.Features.CreateBrand;
using Xunit;

namespace Pam.Operators.UnitTests.Brands.Features.CreateBrand;

public class CreateBrandValidatorTests
{
    private readonly CreateBrandValidator _validator = new();

    [Fact]
    public void Valid_command_passes()
    {
        var command = new CreateBrandCommand("BetAnything EU", "betanything-eu", "EU");

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("UPPERCASE")]
    [InlineData("under_score")]
    [InlineData("space here")]
    [InlineData("a--b")]
    public void Invalid_slug_fails(string slug)
    {
        var command = new CreateBrandCommand("Name", slug, "EU");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateBrandCommand.Slug));
    }

    [Fact]
    public void Slug_with_64_chars_passes()
    {
        var slug = new string('a', 64);
        var command = new CreateBrandCommand("Name", slug, "EU");

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Slug_with_65_chars_fails()
    {
        var slug = new string('a', 65);
        var command = new CreateBrandCommand("Name", slug, "EU");

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("123456789")]
    public void Invalid_jurisdiction_fails(string jurisdiction)
    {
        var command = new CreateBrandCommand("Name", "valid-slug", jurisdiction);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should()
            .Contain(e => e.PropertyName == nameof(CreateBrandCommand.Jurisdiction));
    }

    [Fact]
    public void Empty_name_fails()
    {
        var command = new CreateBrandCommand("", "valid-slug", "EU");

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
