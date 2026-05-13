using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pam.Ingest.Contracts.Transactions.IntegrationEvents;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Data;
using Pam.Ingest.Transactions.EventHandlers;
using Pam.Ingest.Transactions.Models;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.Messaging.Data;
using Pam.Shared.Messaging.Reconciliation;
using Xunit;

namespace Pam.IntegrationTests;

// Reconciler backstop coverage. The reconciler is the defensive net for
// the gap between business COMMIT #1 (IngestDbContext) and messaging
// COMMIT #2 (PamMessagingDbContext via OutboxFlushBehavior) — see ADR #28
// for the atomicity rationale. The test simulates the failure mode by
// inserting a VendorTransaction row directly via EF — bypassing
// TransactionIngestedDomainHandler, which is what would have stamped
// outbox_dispatched_log under the normal command path. Then it invokes
// the reconciler explicitly and asserts the dispatched-log row appears.
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
        // MediatR pipeline so neither the bridge handler nor
        // OutboxFlushBehavior fires. No outbox row, no dispatched-log row.
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

    [Fact]
    public async Task Reconciler_Republishes_Orphans_Past_Per_Cycle_Row_Budget()
    {
        // Regression pin for the bug found 2026-05-13: the previous
        // cursor-based design re-scanned the same prefix of the lookback
        // window on every invocation, so any orphan past
        //   ScanBatchSize * MaxScanBatchesPerCycle
        // rows was permanently invisible (the local cursor reset every
        // call). After the SQL-LEFT-JOIN rewrite the per-cycle budget
        // caps the ORPHAN count per call, not the total scan depth — so
        // every orphan converges in ⌈n/budget⌉ calls.
        //
        // Test shape: tight budget of 100 rows per cycle, 101 orphans.
        // Old code: call 1 republishes 100, call 2 returns 0 → 1 orphan
        // permanently stuck. New code: call 1 republishes 100, call 2
        // republishes 1 → all clear.
        var ct = TestContext.Current.CancellationToken;
        // Inner held for disposal so the analyzer sees a clean ownership
        // chain (CA2000); WithWebHostBuilder returns a wrapping factory
        // that we dispose first.
        await using var inner = new PamApiFactory(containers);
        await using var factory = inner.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            // ScanBatchSize is clamped to >= 100 inside
                            // the reconciler; 100 is the smallest budget
                            // we can pin in a regression test.
                            ["Ingest:Reconciliation:ScanBatchSize"] = "100",
                            ["Ingest:Reconciliation:MaxScanBatchesPerCycle"] = "1",
                        }
                    )
            )
        );
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

        // Insert 101 orphans (1 past the per-cycle budget). UUIDv7 keeps
        // them time-ordered so the reconciler's ORDER BY received_at, id
        // pulls them out in a deterministic, contiguous slice.
        var receivedAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        var orphanIds = Enumerable.Range(0, 101).Select(_ => PamIds.New()).ToArray();
        foreach (var id in orphanIds)
        {
            var tx = VendorTransaction.Record(
                id: id,
                vendorId: "21g",
                vendorReference: $"budget-{id:N}",
                brandId: Guid.NewGuid(),
                playerId: Guid.NewGuid(),
                amountCents: -100L,
                currency: "EUR",
                kind: TransactionKind.Risk,
                occurredAt: receivedAt,
                receivedAt: receivedAt
            );
            tx.ClearDomainEvents();
            ingest.VendorTransactions.Add(tx);
        }
        await ingest.SaveChangesAsync(ct);

        // Run until convergence; cap at a small attempt count so a
        // regression fails fast rather than running forever.
        var ourCovered = 0;
        var ourPks = orphanIds.Select(id => id.ToString("N")).ToHashSet(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await reconciler.ScanAndRepublishAsync(
                minAge: TimeSpan.Zero,
                lookbackWindow: TimeSpan.FromDays(1),
                ct
            );

            ourCovered = await messaging
                .DispatchedLog.AsNoTracking()
                .CountAsync(
                    l =>
                        l.Module == TransactionIngestedDomainHandler.ModuleName
                        && l.EventType == nameof(TransactionIngestedIntegrationEvent)
                        && ourPks.Contains(l.BusinessPk),
                    ct
                );

            if (ourCovered == 101)
            {
                break;
            }
        }

        ourCovered
            .Should()
            .Be(
                101,
                "every orphan must receive a dispatched-log row, even those past the "
                    + "per-cycle scan budget of 100 — that was the bug"
            );
    }
}
