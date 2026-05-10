using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pam.Identity.Contracts.Permissions;
using Pam.Identity.Contracts.Roles;
using Pam.Identity.Data;
using Pam.Identity.Permissions;
using Pam.Identity.Permissions.Models;
using Xunit;

namespace Pam.Identity.UnitTests.Permissions;

// In-memory EF provider is good enough for the resolver — its only relational
// move is a Distinct() projection, which the InMemory provider handles. The
// real DB-backed integration tests land alongside Testcontainers per ROADMAP.
public sealed class PermissionResolverTests : IAsyncDisposable
{
    private readonly ServiceProvider _sp;

    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the ServiceProvider; disposed via DisposeAsync."
    )]
    private readonly IdentityDbContext _db;

    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Owned by the ServiceProvider; disposed via DisposeAsync."
    )]
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    private readonly PermissionResolver _resolver;

    public PermissionResolverTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IdentityDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString())
        );
        services
            .AddIdentityCore<Pam.Identity.Users.Models.BackOfficeUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityDbContext>();
        services.AddScoped<PermissionResolver>();

        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<IdentityDbContext>();
        _roleManager = _sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        _resolver = _sp.GetRequiredService<PermissionResolver>();
    }

    [Fact]
    public async Task Empty_role_list_returns_empty()
    {
        var result = await _resolver.GetPermissionsForRolesAsync(
            [],
            TestContext.Current.CancellationToken
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_role_returns_empty()
    {
        var result = await _resolver.GetPermissionsForRolesAsync(
            ["MadeUpRole"],
            TestContext.Current.CancellationToken
        );

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_role_returns_its_permissions()
    {
        await SeedRoleAsync(
            RoleNames.Operator,
            [PermissionCodes.Operators.BrandsRead, PermissionCodes.Identity.UsersRead]
        );

        var result = await _resolver.GetPermissionsForRolesAsync(
            [RoleNames.Operator],
            TestContext.Current.CancellationToken
        );

        result
            .Should()
            .BeEquivalentTo(
                PermissionCodes.Operators.BrandsRead,
                PermissionCodes.Identity.UsersRead
            );
    }

    [Fact]
    public async Task Multiple_roles_union_their_permissions_distinct()
    {
        await SeedRoleAsync(RoleNames.Operator, [PermissionCodes.Operators.BrandsRead]);
        await SeedRoleAsync(
            RoleNames.Manager,
            [
                PermissionCodes.Operators.BrandsRead, // overlap → de-duped
                PermissionCodes.Operators.BrandsWrite,
                PermissionCodes.Identity.UsersWrite,
            ]
        );

        var result = await _resolver.GetPermissionsForRolesAsync(
            [RoleNames.Operator, RoleNames.Manager],
            TestContext.Current.CancellationToken
        );

        result
            .Should()
            .BeEquivalentTo(
                PermissionCodes.Operators.BrandsRead,
                PermissionCodes.Operators.BrandsWrite,
                PermissionCodes.Identity.UsersWrite
            );
    }

    [Fact]
    public async Task Owner_role_includes_platform_admin()
    {
        await SeedRoleAsync(RoleNames.Owner, [.. PermissionCodes.RoleDefaults.Owner]);

        var result = await _resolver.GetPermissionsForRolesAsync(
            [RoleNames.Owner],
            TestContext.Current.CancellationToken
        );

        result.Should().Contain(PermissionCodes.Platform.Admin);
    }

    private async Task SeedRoleAsync(string roleName, IReadOnlyList<string> permissionCodes)
    {
        var role = new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() };
        await _roleManager.CreateAsync(role);

        foreach (var code in permissionCodes)
        {
            var perm = await _db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (perm is null)
            {
                perm = new Permission { Id = Guid.NewGuid(), Code = code };
                _db.Permissions.Add(perm);
                await _db.SaveChangesAsync();
            }

            _db.RolePermissions.Add(
                new RolePermission { RoleId = role.Id, PermissionId = perm.Id }
            );
        }
        await _db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _sp.DisposeAsync();
    }
}
