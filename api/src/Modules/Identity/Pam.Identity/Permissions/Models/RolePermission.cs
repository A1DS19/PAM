namespace Pam.Identity.Permissions.Models;

// Join entity between IdentityRole<Guid> and Permission. Composite key
// (RoleId, PermissionId) configured via Fluent API.
public sealed class RolePermission
{
    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public Permission Permission { get; set; } = default!;
}
