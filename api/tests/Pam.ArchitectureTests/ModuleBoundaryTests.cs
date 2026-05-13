using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Pam.ArchitectureTests;

// Encode the rule from ARCHITECTURE.md "Modular monolith" section as
// tests so it survives refactoring pressure. A `Pam.<X>` module's
// internal assembly must never depend on another module's internal
// assembly — only on the public `Pam.<Y>.Contracts` projects.
//
// The contracts assembly is the seam that lets us extract a module to
// its own service later: same interface, different transport. If this
// gate fails, you've created the kind of cross-module coupling that
// makes that extraction expensive instead of mechanical.
//
// Assembly-level check (not namespace-prefix): we look at the loaded
// assembly's compile-time references, so `Pam.Operators.Contracts` is
// distinct from `Pam.Operators` — the test mirrors the actual rule
// instead of approximating it via type-name substrings.
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly OperatorsAssembly =
        typeof(Pam.Operators.OperatorsModule).Assembly;
    private static readonly Assembly IdentityAssembly =
        typeof(Pam.Identity.IdentityModule).Assembly;
    private static readonly Assembly NotificationsAssembly =
        typeof(Pam.Notifications.NotificationsModule).Assembly;

    [Fact]
    public void Operators_Does_Not_Depend_On_Identity_Internals() =>
        AssertNoInternalDependency(OperatorsAssembly, "Pam.Identity");

    [Fact]
    public void Operators_Does_Not_Depend_On_Notifications_Internals() =>
        AssertNoInternalDependency(OperatorsAssembly, "Pam.Notifications");

    [Fact]
    public void Identity_Does_Not_Depend_On_Operators_Internals() =>
        AssertNoInternalDependency(IdentityAssembly, "Pam.Operators");

    [Fact]
    public void Notifications_Does_Not_Depend_On_Identity_Internals() =>
        AssertNoInternalDependency(NotificationsAssembly, "Pam.Identity");

    [Fact]
    public void Notifications_Does_Not_Depend_On_Operators_Internals() =>
        AssertNoInternalDependency(NotificationsAssembly, "Pam.Operators");

    private static void AssertNoInternalDependency(Assembly source, string forbiddenModule)
    {
        // Allow Pam.<X>.Contracts; forbid Pam.<X> (and any nested non-
        // Contracts internals). The rule is assembly-name equality on
        // the simple name — namespace prefixes are not the same thing.
        var offender = source
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .FirstOrDefault(name =>
                name is not null
                && (
                    string.Equals(name, forbiddenModule, StringComparison.Ordinal)
                    || (
                        name.StartsWith(forbiddenModule + ".", StringComparison.Ordinal)
                        && !name.StartsWith(
                            forbiddenModule + ".Contracts",
                            StringComparison.Ordinal
                        )
                    )
                )
            );

        offender.Should()
            .BeNull(
                "{0} may only reference {1}.Contracts, not {1} internals. Offending reference: {2}",
                source.GetName().Name,
                forbiddenModule,
                offender ?? "(none)"
            );
    }
}
