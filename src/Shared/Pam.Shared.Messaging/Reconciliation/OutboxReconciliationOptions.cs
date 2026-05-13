namespace Pam.Shared.Messaging.Reconciliation;

public sealed class OutboxReconciliationOptions
{
    public const string SectionName = "Messaging:Reconciliation";

    // How often the BackgroundService wakes to scan every module + run
    // the dispatched-log cleanup pass.
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    // Business rows younger than this are skipped — the normal command
    // path is given a grace window to land the dispatched-log row before
    // the reconciler considers them orphans.
    public TimeSpan MinAge { get; init; } = TimeSpan.FromMinutes(2);

    // Upper bound on how far back the reconciler scans business tables.
    // At millions of rows per day, the bounded scan is what keeps each
    // reconciler pass O(1) in table size — only rows in this window need
    // to be cross-referenced against outbox_dispatched_log. The trade-off
    // is that incidents older than LookbackWindow won't be auto-recovered.
    // Default 2 days covers nearly every conceivable outage (a week-long
    // outage is a human-investigated event, not a reconciler-recoverable
    // one); operators can extend the window manually during incident
    // response if needed.
    public TimeSpan LookbackWindow { get; init; } = TimeSpan.FromDays(2);

    // How long outbox_dispatched_log rows are retained before the
    // cleanup pass deletes them. MUST be >= LookbackWindow (the
    // reconciler relies on dispatched-log rows being present for every
    // business row inside its scan window). Adds a safety margin on top.
    public TimeSpan RetentionWindow { get; init; } = TimeSpan.FromDays(3);

    // Cleanup deletes in bounded batches so a backlog of millions of rows
    // doesn't escalate to a table lock or fill the transaction log.
    // SQL Server's `DELETE TOP (N)` pattern; loop terminates when a pass
    // returns fewer than N rows.
    public int CleanupBatchSize { get; init; } = 10_000;

    // Per-cycle ceiling on cleanup batches. At 10k rows/batch and 50
    // batches max, one cycle can delete up to 500k rows — enough to
    // catch up after a multi-day outage in a few cycles without
    // starving the reconciler.
    public int CleanupMaxBatchesPerCycle { get; init; } = 50;

    // Master switch. Default ON in production; the integration-test
    // fixture disables it so probes don't race the reconciler.
    public bool Enabled { get; init; } = true;
}
