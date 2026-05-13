namespace Pam.Identity.Permissions.Models;

// System-managed reference data. Rows are seeded from PermissionCodes
// (Pam.Identity.Contracts) at startup; not created via API. No audit columns
// — every change is from the seeder, traceable via git.
public sealed class Permission
{
    public Guid Id { get; set; }

    public string Code { get; set; } = default!;

    public string? Description { get; set; }
}
