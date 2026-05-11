using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Pam.Shared.Contracts.DDD;
using Pam.Shared.DDD;
using Xunit;
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace Pam.ArchitectureTests;

// Shape rules that the DDD primitives in Pam.Shared rely on. Without
// these tests the rules drift the moment someone adds an aggregate that
// "almost" looks right but bypasses the Aggregate<T> base class.
public sealed class DomainShapeTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(Pam.Operators.OperatorsModule).Assembly,
        typeof(Pam.Identity.IdentityModule).Assembly,
        typeof(Pam.Notifications.NotificationsModule).Assembly,
    ];

    [Fact]
    public void Domain_Events_Implement_IDomainEvent()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var result = Types
                .InAssembly(assembly)
                .That()
                .HaveNameEndingWith("DomainEvent", StringComparison.Ordinal)
                .And()
                .AreNotAbstract()
                .Should()
                .ImplementInterface(typeof(IDomainEvent))
                .GetResult();

            result
                .IsSuccessful.Should()
                .BeTrue(
                    "{0}: types ending in 'DomainEvent' must implement IDomainEvent. "
                        + "Failing: {1}",
                    assembly.GetName().Name,
                    FormatFailingTypes(result)
                );
        }
    }

    [Fact]
    public void Integration_Events_End_In_IntegrationEvent()
    {
        // The naming convention exists to make a single grep — across the
        // contracts assemblies — find every fact-shaped cross-module event.
        // Trips when someone calls it "FooCreatedEvent" or
        // "FooCreated".
        var contractsAssemblies = new[]
        {
            typeof(Pam.Operators.Contracts.Brands.IntegrationEvents.BrandCreatedIntegrationEvent).Assembly,
        };

        foreach (var assembly in contractsAssemblies)
        {
            var result = Types
                .InAssembly(assembly)
                .That()
                .ResideInNamespaceMatching(@".*\.IntegrationEvents$")
                .And()
                .AreNotAbstract()
                .Should()
                .HaveNameEndingWith("IntegrationEvent", StringComparison.Ordinal)
                .GetResult();

            result
                .IsSuccessful.Should()
                .BeTrue(
                    "{0}: types in *.IntegrationEvents must end in 'IntegrationEvent'. Failing: {1}",
                    assembly.GetName().Name,
                    FormatFailingTypes(result)
                );
        }
    }

    [Fact]
    public void Aggregates_Inherit_From_Aggregate_Or_Entity()
    {
        // Aggregates in the codebase are concrete classes ending in nothing
        // in particular (Brand, BackOfficeUser, ...). The rule we encode:
        // any concrete class that exposes a DomainEvents collection or
        // raises domain events must derive from Aggregate<T>. We approximate
        // this by checking that types implementing IAggregate also derive
        // from Aggregate<T> (where the dispatcher reads DomainEvents from).
        foreach (var assembly in ModuleAssemblies)
        {
            var result = Types
                .InAssembly(assembly)
                .That()
                .ImplementInterface(typeof(IAggregate))
                .And()
                .AreNotAbstract()
                .Should()
                .Inherit(typeof(Aggregate<>))
                .GetResult();

            // NetArchTest's Inherit check is brittle against open generics —
            // a concrete `Aggregate<Guid>` should pass. Allow an empty
            // assembly (no aggregates) to count as success.
            if (result.FailingTypeNames is null)
            {
                continue;
            }

            result
                .IsSuccessful.Should()
                .BeTrue(
                    "{0}: types implementing IAggregate must derive from Aggregate<>. Failing: {1}",
                    assembly.GetName().Name,
                    FormatFailingTypes(result)
                );
        }
    }

    private static string FormatFailingTypes(ArchTestResult result) =>
        result.FailingTypeNames is null or { Count: 0 }
            ? "(no failing types reported)"
            : string.Join(", ", result.FailingTypeNames);
}
