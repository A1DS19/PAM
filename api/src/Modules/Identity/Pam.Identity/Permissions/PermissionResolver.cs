using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pam.Identity.Data;

namespace Pam.Identity.Permissions;

// Resolves the flat list of permission codes a user has via their assigned
// roles. Used by AuthorizationController during token issuance to project
// permission claims into the access token.
//
// Direct user-permission grants (bypassing roles) are not modeled today; if
// they're needed later, add a UserPermission join and union here.
public sealed class PermissionResolver(
    IdentityDbContext db,
    RoleManager<IdentityRole<Guid>> roleManager
)
{
    public async Task<IReadOnlyList<string>> GetPermissionsForRolesAsync(
        IList<string> roleNames,
        CancellationToken ct
    )
    {
        if (roleNames.Count == 0)
        {
            return [];
        }

        var roleIds = await roleManager
            .Roles.Where(r => r.Name != null && roleNames.Contains(r.Name))
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (roleIds.Count == 0)
        {
            return [];
        }

        return await db
            .RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToListAsync(ct);
    }
}
