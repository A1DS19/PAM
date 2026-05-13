namespace Pam.Shared.Messaging.Reconciliation;

// Module-level reconciler that scans a module's business tables for rows
// whose integration event never landed in messaging.outbox_dispatched_log,
// and republishes them through the same atomic outbox path.
//
// Each publishing module implements one and registers it with
// services.AddSingleton<IOutboxReconciler, MyModuleReconciler>() — the
// OutboxReconciliationService BackgroundService iterates every registered
// instance every Interval.
//
// Republishes go through IPublishEndpoint.Publish + OutboxDispatchedLog.Add
// + PamMessagingDbContext.SaveChangesAsync — same pattern as the bridge
// handler that should have written the row on the original command path.
// At-least-once delivery; consumers must be idempotent (inbox dedupe via
// MT's UseEntityFrameworkOutbox on the receive endpoint).
public interface IOutboxReconciler
{
    string ModuleName { get; }

    // Scans business rows in the window
    //   (now - lookbackWindow) < received_at < (now - minAge)
    // for entries whose outbox_dispatched_log row is missing, and
    // republishes them. Returns the number of events republished.
    //
    // - minAge gives the normal command path a grace window to commit
    //   the dispatched-log row before the reconciler intervenes.
    // - lookbackWindow bounds the scan size — at millions of business
    //   rows per day, an unbounded scan would dominate the loop on big
    //   tables. Incidents older than lookbackWindow are out of the
    //   reconciler's auto-recovery scope and require manual remediation
    //   (extend the window temporarily during incident response if
    //   needed).
    Task<int> ScanAndRepublishAsync(
        TimeSpan minAge,
        TimeSpan lookbackWindow,
        CancellationToken cancellationToken
    );
}
