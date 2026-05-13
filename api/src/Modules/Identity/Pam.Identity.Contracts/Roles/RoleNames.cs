using System.Collections.Immutable;

namespace Pam.Identity.Contracts.Roles;

// Well-known back-office role names. Seeded into `identity.roles` at startup.
// Referenced by validators (role-name allowlist) and by tests; treat as a
// closed enum until a real customer asks for custom roles.
public static class RoleNames
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Operator = "Operator";
    public const string Accountant = "Accountant";

    public static ImmutableArray<string> All { get; } = [Owner, Manager, Operator, Accountant];
}
