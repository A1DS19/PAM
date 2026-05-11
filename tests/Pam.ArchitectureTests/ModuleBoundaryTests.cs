using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;
using ArchTestResult = NetArchTest.Rules.TestResult;

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
public sealed class ModuleBoundaryTests
{
    private static readonly Assembly OperatorsAssembly =
        typeof(Pam.Operators.OperatorsModule).Assembly;
    private static readonly Assembly IdentityAssembly =
        typeof(Pam.Identity.IdentityModule).Assembly;
    private static readonly Assembly NotificationsAssembly =
        typeof(Pam.Notifications.NotificationsModule).Assembly;

    [Fact]
    public void Operators_Does_Not_Depend_On_Identity_Internals()
    {
        var result = Types
            .InAssembly(OperatorsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pam.Identity")
            .GetResult();

        result
            .IsSuccessful.Should()
            .BeTrue(
                "Pam.Operators may only reach into Pam.Identity.Contracts, not Pam.Identity. "
                    + "Failing types: {0}",
                FormatFailingTypes(result)
            );
    }

    [Fact]
    public void Operators_Does_Not_Depend_On_Notifications_Internals()
    {
        var result = Types
            .InAssembly(OperatorsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pam.Notifications")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailingTypes(result));
    }

    [Fact]
    public void Identity_Does_Not_Depend_On_Operators_Internals()
    {
        var result = Types
            .InAssembly(IdentityAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pam.Operators")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailingTypes(result));
    }

    [Fact]
    public void Notifications_Does_Not_Depend_On_Identity_Internals()
    {
        var result = Types
            .InAssembly(NotificationsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pam.Identity")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailingTypes(result));
    }

    [Fact]
    public void Notifications_Does_Not_Depend_On_Operators_Internals()
    {
        var result = Types
            .InAssembly(NotificationsAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pam.Operators")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(FormatFailingTypes(result));
    }

    private static string FormatFailingTypes(ArchTestResult result) =>
        result.FailingTypeNames is null or { Count: 0 }
            ? "(no failing types reported)"
            : string.Join(", ", result.FailingTypeNames);
}
