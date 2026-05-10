using FluentAssertions;
using Pam.Identity.Authentication.Mfa;
using Xunit;

namespace Pam.Identity.UnitTests.Authentication.Mfa;

public class MfaDisableValidatorTests
{
    private readonly MfaDisableValidator _validator = new();

    [Fact]
    public void Valid_password_passes()
    {
        _validator.Validate(new MfaDisableCommand("CurrentPass123!")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_password_fails()
    {
        _validator.Validate(new MfaDisableCommand("")).IsValid.Should().BeFalse();
    }
}
