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

    // Scans for business rows whose dispatched-log entry is missing AND
    // whose business row is older than minAge. Returns the number of
    // events republished. The age threshold gives the normal command path
    // a grace window to commit before the reconciler intervenes — without
    // it, a row that just landed but whose outbox commit is in flight
    // would race the reconciler.
    Task<int> ScanAndRepublishAsync(TimeSpan minAge, CancellationToken cancellationToken);
}
