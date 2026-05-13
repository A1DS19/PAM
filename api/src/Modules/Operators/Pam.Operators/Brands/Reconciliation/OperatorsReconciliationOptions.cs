namespace Pam.Operators.Brands.Reconciliation;

// Brand creation volume is trivial compared to vendor ingest — a handful
// of rows per year per region, not millions per day. Batch sizes are
// sized accordingly so each scan touches at most a few hundred rows.
public sealed class OperatorsReconciliationOptions
{
    public const string SectionName = "Operators:Reconciliation";

    public int ScanBatchSize { get; init; } = 200;

    public int MaxScanBatchesPerCycle { get; init; } = 5;
}
