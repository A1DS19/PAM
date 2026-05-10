using FluentAssertions;
using Pam.Identity.Contracts.Roles;
using Pam.Identity.Users.Features.ListUsers;
using Xunit;

namespace Pam.Identity.UnitTests.Users.Features.ListUsers;

public class ListUsersValidatorTests
{
    private readonly ListUsersValidator _validator = new();

    [Fact]
    public void Default_paging_passes()
    {
        var query = new ListUsersQuery(1, 50, BrandId: null, Role: null, LockedOut: null);

        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_below_1_fails(int page)
    {
        var query = new ListUsersQuery(page, 50, null, null, null);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void PageSize_outside_1_200_fails(int size)
    {
        var query = new ListUsersQuery(1, size, null, null, null);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Unknown_role_filter_fails()
    {
        var query = new ListUsersQuery(1, 50, null, "MadeUpRole", null);

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Known_role_filter_passes()
    {
        var query = new ListUsersQuery(1, 50, null, RoleNames.Owner, null);

        _validator.Validate(query).IsValid.Should().BeTrue();
    }
}
