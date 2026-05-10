using System.Collections.Immutable;

namespace Pam.Identity.Contracts.Permissions;

// Well-known permission codes referenced by [Authorize(Policy = ...)] across
// modules. Codes are stable strings (lowercase dotted), seeded into the
// `identity.permissions` table on startup. Adding a code here is the contract
// for granting it; the seeder reads `All` reflectively.
//
// Convention: <module>.<resource>.<verb> — e.g. `operators.brands.write`.
// Pure read access can collapse to <module>.<resource>.read; coarser
// "everything in this module" wraps under <module>.admin.
public static class PermissionCodes
{
    public static class Operators
    {
        public const string BrandsRead = "operators.brands.read";
        public const string BrandsWrite = "operators.brands.write";
    }

    public static class Identity
    {
        public const string UsersRead = "identity.users.read";
        public const string UsersWrite = "identity.users.write";
        public const string RolesWrite = "identity.roles.write";
    }

    public static class Platform
    {
        // Bypasses the brand-scoped global query filter once that lands.
        // Reserved for the Owner role.
        public const string Admin = "platform.admin";
    }

    // The full set, used by the seeder to ensure the `permissions` table.
    // Add new codes above and they become available automatically.
    public static ImmutableArray<string> All { get; } =
    [
        Operators.BrandsRead,
        Operators.BrandsWrite,
        Identity.UsersRead,
        Identity.UsersWrite,
        Identity.RolesWrite,
        Platform.Admin,
    ];

    // Default per-role grants used by the role-permission seeder. Owners get
    // everything; the rest get progressively narrower slices. Operators of a
    // specific brand do not get `platform.admin` — that elevates past the
    // brand-scoped query filter when it lands.
    public static class RoleDefaults
    {
        public static ImmutableArray<string> Owner { get; } = All;

        public static ImmutableArray<string> Manager { get; } =
        [
            Operators.BrandsRead,
            Operators.BrandsWrite,
            Identity.UsersRead,
            Identity.UsersWrite,
            Identity.RolesWrite,
        ];

        public static ImmutableArray<string> Operator { get; } =
        [Operators.BrandsRead, Identity.UsersRead];

        public static ImmutableArray<string> Accountant { get; } = [Operators.BrandsRead];
    }
}
