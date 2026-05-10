using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Pam.Identity.Contracts.Permissions;
using Pam.Identity.Contracts.Roles;
using Pam.Identity.Data;
using Pam.Identity.Permissions.Models;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.Identity;
// `Pam.Identity.Permissions` (the module's permission types) shadows
// `OpenIddictConstants.Permissions` (the OAuth permission constants). Alias
// the OpenIddict ones for clarity at the call sites.
using OidcClientTypes = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes;
using OidcConsentTypes = OpenIddict.Abstractions.OpenIddictConstants.ConsentTypes;
using OidcPermissions = OpenIddict.Abstractions.OpenIddictConstants.Permissions;
using OidcRequirements = OpenIddict.Abstractions.OpenIddictConstants.Requirements;

namespace Pam.Identity.Seeding;

// Idempotent on every startup. Each step skips if its precondition is met,
// so re-running is cheap and safe. Order matters: permissions before
// role-permission grants; the SPA application before the bootstrap Owner can
// log in.
public sealed class IdentitySeeder(
    IdentityDbContext db,
    UserManager<BackOfficeUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager,
    IOptions<BackOfficeSpaOptions> spaOptions,
    ILogger<IdentitySeeder> logger
)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedPermissionsAsync(ct);
        await SeedRolesAsync();
        await SeedRolePermissionsAsync(ct);
        await SeedScopesAsync(ct);
        await SeedApplicationsAsync(ct);
        await SeedBootstrapOwnerAsync(ct);
    }

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.Select(p => p.Code).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

        var toAdd = PermissionCodes
            .All.Where(code => !existingSet.Contains(code))
            .Select(code => new Permission
            {
                Id = Guid.CreateVersion7(),
                Code = code,
                Description = null,
            })
            .ToList();

        if (toAdd.Count == 0)
        {
            return;
        }

        db.Permissions.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} permission codes", toAdd.Count);
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in RoleNames.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(
                new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() }
            );
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to create role '{role}': {string.Join("; ", result.Errors.Select(e => e.Description))}"
                );
            }
            logger.LogInformation("Seeded role {Role}", role);
        }
    }

    private async Task SeedRolePermissionsAsync(CancellationToken ct)
    {
        var roleMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RoleNames.Owner] = PermissionCodes.RoleDefaults.Owner,
            [RoleNames.Manager] = PermissionCodes.RoleDefaults.Manager,
            [RoleNames.Operator] = PermissionCodes.RoleDefaults.Operator,
            [RoleNames.Accountant] = PermissionCodes.RoleDefaults.Accountant,
        };

        var permissionsByCode = await db
            .Permissions.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);

        foreach (var (roleName, codes) in roleMap)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            var existingPermissionIds = await db
                .RolePermissions.Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);
            var existingSet = new HashSet<Guid>(existingPermissionIds);

            var toAdd = codes
                .Where(c => permissionsByCode.ContainsKey(c))
                .Select(c => permissionsByCode[c])
                .Where(id => !existingSet.Contains(id))
                .Select(id => new RolePermission { RoleId = role.Id, PermissionId = id })
                .ToList();

            if (toAdd.Count == 0)
            {
                continue;
            }

            db.RolePermissions.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Granted {Count} permissions to role {Role}",
                toAdd.Count,
                roleName
            );
        }
    }

    private async Task SeedScopesAsync(CancellationToken ct)
    {
        if (await scopeManager.FindByNameAsync(IdentityModule.PamApiScope, ct) is not null)
        {
            return;
        }

        await scopeManager.CreateAsync(
            new OpenIddictScopeDescriptor
            {
                Name = IdentityModule.PamApiScope,
                DisplayName = "PAM API access",
                Resources = { "pam-api" },
            },
            ct
        );
        logger.LogInformation("Seeded scope {Scope}", IdentityModule.PamApiScope);
    }

    private async Task SeedApplicationsAsync(CancellationToken ct)
    {
        var spa = spaOptions.Value;
        if (await applicationManager.FindByClientIdAsync(spa.ClientId, ct) is not null)
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = spa.ClientId,
            ClientType = OidcClientTypes.Public,
            ConsentType = OidcConsentTypes.Implicit,
            DisplayName = spa.DisplayName,
            Permissions =
            {
                OidcPermissions.Endpoints.Authorization,
                OidcPermissions.Endpoints.EndSession,
                OidcPermissions.Endpoints.Token,
                OidcPermissions.Endpoints.PushedAuthorization,
                OidcPermissions.GrantTypes.AuthorizationCode,
                OidcPermissions.GrantTypes.RefreshToken,
                OidcPermissions.ResponseTypes.Code,
                OidcPermissions.Scopes.Email,
                OidcPermissions.Scopes.Profile,
                OidcPermissions.Scopes.Roles,
                OidcPermissions.Prefixes.Scope + IdentityModule.PamApiScope,
            },
            Requirements = { OidcRequirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var uri in spa.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }
        foreach (var uri in spa.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
        }

        await applicationManager.CreateAsync(descriptor, ct);
        logger.LogInformation("Seeded back-office SPA application {ClientId}", spa.ClientId);
    }

    // The bootstrap Owner is created on first run if no Owner exists yet AND
    // both env vars are set. After first run the env vars are ignored. The
    // operator should remove them from the deployment environment once the
    // Owner has logged in and rotated their password.
    private async Task SeedBootstrapOwnerAsync(CancellationToken ct)
    {
        var ownersInRole = await userManager.GetUsersInRoleAsync(RoleNames.Owner);
        if (ownersInRole.Count > 0)
        {
            return;
        }

        var email = Environment.GetEnvironmentVariable("PAM_BOOTSTRAP_OWNER_EMAIL");
        var password = Environment.GetEnvironmentVariable("PAM_BOOTSTRAP_OWNER_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No Owner exists and PAM_BOOTSTRAP_OWNER_EMAIL / PAM_BOOTSTRAP_OWNER_PASSWORD are not set — back-office login will fail until an Owner is provisioned."
            );
            return;
        }

        var user = new BackOfficeUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create bootstrap Owner: {string.Join("; ", createResult.Errors.Select(e => e.Description))}"
            );
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Owner);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign Owner role: {string.Join("; ", roleResult.Errors.Select(e => e.Description))}"
            );
        }

        // Stamp audit cols directly — UserManager.CreateAsync goes through EF
        // outside our normal scope, so the AuditableSaveChangesInterceptor
        // sees the Actor.System we're running as. This is just for clarity.
        _ = ActorType.System;

        logger.LogInformation("Seeded bootstrap Owner {Email}", email);
    }
}
