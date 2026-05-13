namespace Pam.Shared.Messaging.Reconciliation;

public sealed class OutboxReconciliationOptions
{
    public const string SectionName = "Messaging:Reconciliation";

    // How often the BackgroundService wakes to scan every module.
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    // Business rows younger than this are skipped — the normal command
    // path is given a grace window to land the dispatched-log row.
    public TimeSpan MinAge { get; init; } = TimeSpan.FromMinutes(2);

    // Master switch. Default ON in production; the integration-test fixture
    // disables it so probes don't race the reconciler.
    public bool Enabled { get; init; } = true;
}
