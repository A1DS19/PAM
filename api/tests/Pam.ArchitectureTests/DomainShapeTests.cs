using System.Reflection;
using FluentAssertions;
using MediatR;
using NetArchTest.Rules;
using Pam.Shared.Contracts.DDD;
using Pam.Shared.DDD;
using Pam.Shared.Messaging.Data;
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
        typeof(Pam.Ingest.IngestModule).Assembly,
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

    [Fact]
    public void Bridge_Handlers_Depend_On_PamMessagingDbContext()
    {
        // Every bridge handler — the class that translates a domain event
        // to an integration event — must inject PamMessagingDbContext so
        // it can write the outbox_dispatched_log row alongside the
        // IPublishEndpoint.Publish call. Without that row, the per-module
        // IOutboxReconciler cannot detect orphans (business row committed,
        // integration event never delivered) and the sub-ms under-deliver
        // window described in ADR #28 stays uncovered.
        //
        // NetArchTest doesn't introspect constructor parameters, so this
        // check is plain reflection: find every concrete handler for
        // DomainEventNotification<T> and verify it has a constructor
        // parameter of type PamMessagingDbContext.
        var notificationHandlerOpenGeneric = typeof(INotificationHandler<>);
        var domainEventNotificationOpenGeneric = typeof(DomainEventNotification<>);

        var offenders = new List<string>();

        foreach (var assembly in ModuleAssemblies)
        {
            var bridgeHandlers = assembly
                .GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && t.GetInterfaces()
                        .Any(i =>
                            i.IsGenericType
                            && i.GetGenericTypeDefinition() == notificationHandlerOpenGeneric
                            && i.GenericTypeArguments[0].IsGenericType
                            && i.GenericTypeArguments[0].GetGenericTypeDefinition()
                                == domainEventNotificationOpenGeneric
                        )
                );

            foreach (var handler in bridgeHandlers)
            {
                var hasMessagingDep = handler
                    .GetConstructors()
                    .Any(c =>
                        c.GetParameters().Any(p => p.ParameterType == typeof(PamMessagingDbContext))
                    );

                if (!hasMessagingDep)
                {
                    offenders.Add(handler.FullName ?? handler.Name);
                }
            }
        }

        offenders.Should()
            .BeEmpty(
                "every bridge handler must depend on PamMessagingDbContext so it can write the "
                    + "outbox_dispatched_log row alongside IPublishEndpoint.Publish (ADR #28). "
                    + "Offenders: {0}",
                string.Join(", ", offenders)
            );
    }

    private static string FormatFailingTypes(ArchTestResult result) =>
        result.FailingTypeNames is null or { Count: 0 }
            ? "(no failing types reported)"
            : string.Join(", ", result.FailingTypeNames);
}
