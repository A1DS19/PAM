using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pam.Ingest.Contracts.Transactions.IntegrationEvents;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Data;
using Pam.Ingest.Transactions.EventHandlers;
using Pam.Ingest.Transactions.Models;
using Pam.Shared.Messaging.Data;
using Pam.Shared.Messaging.Reconciliation;
using Xunit;

namespace Pam.IntegrationTests;

// Reconciler backstop coverage. The reconciler is the defensive net for
// any failure mode the atomic transaction can't cover (hardware faults,
// future regressions to the atomicity invariant). The test simulates the
// failure mode by inserting a VendorTransaction row directly via EF —
// bypassing TransactionIngestedDomainHandler, which is what would have
// stamped outbox_dispatched_log under the normal command path. Then it
// invokes the reconciler explicitly and asserts the dispatched-log row
// appears.
//
// The reconciliation BackgroundService is disabled in PamApiFactory so it
// doesn't race the test; this exercises IngestOutboxReconciler directly.
[Collection(nameof(PamContainersCollection))]
public sealed class OutboxReconciliationTests(PamContainersFixture containers)
{
    [Fact]
    public async Task Reconciler_Republishes_Orphan_VendorTransactions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        // Boot the host so migrations run + DI graph is built. The
        // HttpClient itself isn't used; this test drives the reconciler
        // through DI.
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var ingest = sp.GetRequiredService<IngestDbContext>();
        var messaging = sp.GetRequiredService<PamMessagingDbContext>();
        var reconciler = sp
            .GetServices<IOutboxReconciler>()
            .Single(r =>
                string.Equals(
                    r.ModuleName,
                    TransactionIngestedDomainHandler.ModuleName,
                    StringComparison.Ordinal
                )
            );

        var orphanId = Guid.NewGuid();
        var orphanReference = $"recon-{Guid.NewGuid():N}";

        // Force the orphan state: insert the business row outside the
        // MediatR pipeline so neither the bridge handler nor the atomic
        // transaction fires. No outbox row, no dispatched-log row.
        var orphan = VendorTransaction.Record(
            id: orphanId,
            vendorId: "21g",
            vendorReference: orphanReference,
            brandId: Guid.NewGuid(),
            playerId: Guid.NewGuid(),
            amountCents: -1050L,
            currency: "EUR",
            kind: TransactionKind.Risk,
            occurredAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            receivedAt: DateTimeOffset.UtcNow.AddMinutes(-10)
        );
        // Discard the domain events — bypassing the dispatcher is the
        // whole point of the simulation.
        orphan.ClearDomainEvents();
        ingest.VendorTransactions.Add(orphan);
        await ingest.SaveChangesAsync(ct);

        // Precondition: no dispatched-log row exists for this orphan.
        var businessPk = orphanId.ToString("N");
        var preLog = await messaging.DispatchedLog.AsNoTracking().FirstOrDefaultAsync(
            l => l.BusinessPk == businessPk,
            ct
        );
        preLog.Should().BeNull("the orphan was inserted outside the bridge-handler path");

        // Lookback set generously so the test orphans (received_at
        // ~10 minutes ago) fall inside the scan window regardless of
        // how long the container fixture has been alive.
        var republished = await reconciler.ScanAndRepublishAsync(
            minAge: TimeSpan.Zero,
            lookbackWindow: TimeSpan.FromDays(1),
            ct
        );

        // Other tests in the same shared container fixture may have left
        // their own orphans behind, so assert "at least one" rather than
        // an absolute count.
        republished
            .Should()
            .BeGreaterOrEqualTo(1, "the reconciler must pick up at least our orphan");

        var postLog = await messaging.DispatchedLog.AsNoTracking().FirstOrDefaultAsync(
            l =>
                l.BusinessPk == businessPk
                && l.EventType == nameof(TransactionIngestedIntegrationEvent),
            ct
        );
        postLog.Should().NotBeNull(
            "the reconciler writes the dispatched-log row in the same SaveChanges as the republish"
        );
    }

    [Fact]
    public async Task Reconciler_Skips_Rows_With_Dispatched_Log_Entry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new PamApiFactory(containers);
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var ingest = sp.GetRequiredService<IngestDbContext>();
        var messaging = sp.GetRequiredService<PamMessagingDbContext>();
        var reconciler = sp
            .GetServices<IOutboxReconciler>()
            .Single(r =>
                string.Equals(
                    r.ModuleName,
                    TransactionIngestedDomainHandler.ModuleName,
                    StringComparison.Ordinal
                )
            );

        var coveredId = Guid.NewGuid();
        var covered = VendorTransaction.Record(
            id: coveredId,
            vendorId: "21g",
            vendorReference: $"covered-{Guid.NewGuid():N}",
            brandId: Guid.NewGuid(),
            playerId: Guid.NewGuid(),
            amountCents: 500L,
            currency: "EUR",
            kind: TransactionKind.Win,
            occurredAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            receivedAt: DateTimeOffset.UtcNow.AddMinutes(-10)
        );
        covered.ClearDomainEvents();
        ingest.VendorTransactions.Add(covered);
        await ingest.SaveChangesAsync(ct);

        // Pre-populate the dispatched-log row as if the bridge handler had run.
        messaging.DispatchedLog.Add(
            new OutboxDispatchedLog
            {
                Module = TransactionIngestedDomainHandler.ModuleName,
                BusinessPk = coveredId.ToString("N"),
                EventType = nameof(TransactionIngestedIntegrationEvent),
                DispatchedAt = DateTimeOffset.UtcNow,
            }
        );
        await messaging.SaveChangesAsync(ct);

        var republishedBefore = await reconciler.ScanAndRepublishAsync(
            minAge: TimeSpan.Zero,
            lookbackWindow: TimeSpan.FromDays(1),
            ct
        );
        var coveredPk = coveredId.ToString("N");

        // After a first sweep, every prior orphan is covered. A second
        // sweep must republish nothing — the covered row stays covered,
        // and there's no double-publish.
        var republishedAfter = await reconciler.ScanAndRepublishAsync(
            minAge: TimeSpan.Zero,
            lookbackWindow: TimeSpan.FromDays(1),
            ct
        );

        republishedAfter
            .Should()
            .Be(0, "two back-to-back sweeps must converge — nothing remains orphaned");

        // The originally-covered row never produced a duplicate
        // dispatched-log entry. The PK is composite + unique, so a
        // double-write would have thrown above; this asserts there's
        // exactly one row for our specific business_pk.
        var coveredLogRowCount = await messaging
            .DispatchedLog.AsNoTracking()
            .CountAsync(
                l =>
                    l.BusinessPk == coveredPk
                    && l.EventType == nameof(TransactionIngestedIntegrationEvent),
                ct
            );

        coveredLogRowCount
            .Should()
            .Be(1, "the pre-existing dispatched-log row must not be duplicated");

        // Keep the unused 'before' value referenced so the variable is
        // obviously part of the test's intent rather than dead code.
        _ = republishedBefore;
    }
}
