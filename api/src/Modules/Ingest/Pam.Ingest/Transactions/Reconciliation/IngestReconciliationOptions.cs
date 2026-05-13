namespace Pam.Ingest.Transactions.Reconciliation;

public sealed class IngestReconciliationOptions
{
    public const string SectionName = "Ingest:Reconciliation";

    // Max candidate rows pulled per query pass.
    public int ScanBatchSize { get; init; } = 2_000;

    // Max query passes in one reconciliation cycle.
    public int MaxScanBatchesPerCycle { get; init; } = 20;
}
