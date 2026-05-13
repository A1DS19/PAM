# DB Scaling

Operational guide for sustained high-volume ingest (millions of
transactions per day).

## Scope

- `ingest.vendor_transactions`
- `messaging.outbox_dispatched_log`

## Current guarantees

- Reconciliation scans are bounded by time window and batch controls.
- `outbox_dispatched_log` is retention-cleaned in bounded delete batches.
- Ingest idempotency is insert-first, enforced by a unique index.

## Required production settings (starting point)

`appsettings`:

```json
{
  "Messaging": {
    "Reconciliation": {
      "Interval": "00:05:00",
      "MinAge": "00:02:00",
      "LookbackWindow": "2.00:00:00",
      "RetentionWindow": "3.00:00:00",
      "CleanupBatchSize": 10000,
      "CleanupMaxBatchesPerCycle": 50
    }
  },
  "Ingest": {
    "Reconciliation": {
      "ScanBatchSize": 2000,
      "MaxScanBatchesPerCycle": 20
    }
  }
}
```

Tune by workload:

- Increase `CleanupBatchSize`/`CleanupMaxBatchesPerCycle` if
  `outbox_dispatched_log` backlog grows.
- Increase `ScanBatchSize`/`MaxScanBatchesPerCycle` during incident
  recovery when orphan backlog must be drained faster.
- Keep `RetentionWindow >= LookbackWindow`.

## Partitioning plan for `ingest.vendor_transactions` (SQL Server)

This table is append-only and regulator-retained. It must be partitioned
by `received_at` before long-running production scale.

### Strategy

- **Weekly** partitions on `received_at` (`datetimeoffset`), ISO-8601 weeks
  starting Monday 00:00 UTC. Initial horizon preallocated 2026-01-05 →
  2036-01-06 (~522 boundaries; SQL Server cap is 15 000). Weekly is the
  sweet spot at multi-million rows/day: small enough that per-partition
  rebuild/stats stay cheap, large enough that partition-plan overhead
  doesn't matter.
- Partition-aligned indexes use `STATISTICS_INCREMENTAL = ON` so per-
  partition stats refresh stays O(hot-rows). The unique idempotency index
  on `(vendor_id, vendor_reference)` is deliberately **non-aligned**
  (global UNIQUE) — see *Archival decision pending* below.
- Sliding window (not yet wired — pending the idempotency-index decision):
  - hot recent partitions on fast filegroup
  - older partitions switched to archive table via `SWITCH PARTITION`
  - archived partitions dropped only after regulatory retention expires

### Landed in code

- Migration
  `20260513093000_PrepareVendorTransactionsPartitioning` creates:
  - partition function
    `pf_ingest_vendor_transactions_received_at_weekly`
  - partition scheme
    `ps_ingest_vendor_transactions_received_at_weekly`
  - partitioned shadow table `ingest.vendor_transactions_p`
    with matching indexes for backfill-and-swap cutover.
- Migration
  `20260513094500_CutoverVendorTransactionsPartitionedTable` performs
  the guarded swap (`vendor_transactions_p` -> `vendor_transactions`)
  with prechecks (table existence + row-count parity).
- Migration
  `20260513101500_AddPartitionBackfillProcedureAndSqlAgentJob` creates:
  - `ingest.usp_partition_backfill_step` (resumable incremental copy)
  - SQL Server Agent job `pam_ingest_partition_backfill`
    scheduled every 5 minutes — runs **until cutover completes**, then
    the SP no-ops because the shadow table is gone.
- Migration
  `20260513120000_AddPartitionMaintenanceProcedureAndSqlAgentJob` creates:
  - `ingest.usp_partition_maintain_vendor_transactions` —
    idempotent SP that:
    - SPLITs future weekly boundaries to maintain at least
      `@future_weeks_to_keep` (default 12) ahead of `GETUTCDATE()`.
      Defensive even though Prepare preallocates ~10 years; guarantees
      the system never silently writes to an unbounded terminal
      partition no matter how long the deployment lives.
    - `UPDATE STATISTICS WITH RESAMPLE ON PARTITIONS (hot-partition)` —
      cheap thanks to `STATISTICS_INCREMENTAL = ON` on the aligned
      indexes.

  Scheduling lives in C#, not SQL Agent —
  `PartitionMaintenanceService` (`BackgroundService` under
  `Pam.Ingest.Transactions.Partitioning`) runs the SP once at startup
  and every 24 hours thereafter via `PeriodicTimer`. Errors are logged
  and the next tick retries. No Agent dependency; portable to
  Linux/k8s deployments. The SP can still be invoked ad-hoc by a DBA
  for surgical interventions.

### Archival decision pending

`SWITCH PARTITION` (the metadata-only path for sliding-window
archival) requires **every index on the partitioned table to be
partition-aligned**. Today the unique idempotency index
`ix_vendor_transactions_idempotency(vendor_id, vendor_reference)` is
intentionally non-aligned — uniqueness must be **global** across all
weekly partitions or vendor retries that fall in a different week
would re-insert.

Three resolution paths, each with trade-offs:

1. **Make the idempotency index partition-aligned.** Breaks
   correctness: uniqueness becomes per-partition only. Rejected.
2. **Drop the non-aligned index before SWITCH, recreate after.**
   Full index rebuild on hundreds of millions of rows per archival
   cycle; leaves a window where uniqueness isn't enforced. Slow,
   risky, but no schema change.
3. **Lift idempotency keys into a small companion table**
   `ingest.vendor_transaction_keys(vendor_id, vendor_reference,
   transaction_id)` with the UNIQUE constraint there. Handler
   inserts to both inside the same business transaction; archival
   on `vendor_transactions` then uses `SWITCH PARTITION` freely.
   The keys table is small (one row per ingest event, no payload)
   and can be aged out on a separate, looser cadence — vendor
   retries effectively stop happening after days, not years.

Recommended path: **3**, scheduled before the first archival cycle
becomes necessary. The current monthly load doesn't force the
issue yet — the maintenance job covers the in-band lifecycle work
that does.

### Cutover sequence

1. Create partition function + partition scheme for **weekly** boundaries.
2. Create a new partitioned table `ingest.vendor_transactions_p` with the
   same columns/constraints. Partition-aligned indexes get
   `STATISTICS_INCREMENTAL = ON`.
3. Recreate required indexes on the partitioned table (aligned to scheme).
4. Let SQL Server Agent run `ingest.usp_partition_backfill_step`
   continuously until parity is reached (`live_count == shadow_count`).
5. Write freeze window (short maintenance window). Ensure no writers
   insert into `ingest.vendor_transactions` between final parity check
   and swap. The cutover SP's `COUNT(*)` check is **not** lock-taking
   on its own; the operator must drain the SOAP listener (or take a
   `TABLOCKX`) before invoking the SP.
6. Rename tables (`vendor_transactions` -> `_old`, `_p` -> live name).
7. Repoint/rebuild indexes and verify query plans.
8. Keep old table read-only for rollback window, then decommission.

### Backfill runbook (automated)

1. Apply migration `20260513101500_AddPartitionBackfillProcedureAndSqlAgentJob`.
2. Confirm Agent job exists/enabled:
   - `msdb.dbo.sysjobs` contains `pam_ingest_partition_backfill`.
3. Monitor parity:
   - `SELECT COUNT_BIG(*) FROM ingest.vendor_transactions`
   - `SELECT COUNT_BIG(*) FROM ingest.vendor_transactions_p`
4. When counts converge, start write freeze and immediately apply migration
   `20260513094500_CutoverVendorTransactionsPartitionedTable`.

### Ongoing operation

Two distinct lifecycles, intentionally on different cadences:

**Automated (daily, via `PartitionMaintenanceService` in C#):**

1. Extend future weekly boundaries via SPLIT so the partition function
   always has ≥12 weeks ahead of "now" (configurable via
   `@future_weeks_to_keep` on the SP). Idempotent — re-runs are no-ops
   when the horizon is already healthy.
2. Refresh statistics on the hot partition only
   (`UPDATE STATISTICS ... WITH RESAMPLE, ON PARTITIONS (n)`).

**Manual (until the archival decision lands):**

3. Switch oldest hot partition to archive table/filegroup — blocked on
   the *Archival decision pending* item above.
4. Rebuild/reorganize active partition indexes as needed — operator
   judgement; not automated. Use `sys.dm_db_index_physical_stats`.
5. Drop archive partitions beyond compliance retention — also blocked
   on the archival decision.

## Observability to keep

- Rows deleted per cleanup cycle (`outbox_dispatched_log`).
- Reconciler republished count per cycle.
- Oldest `vendor_transactions.received_at` missing dispatched log.
- DB file growth by filegroup.
- Index fragmentation for active `vendor_transactions` partitions.

## SQL Server-specialized options worth using

1. Partition switching (`ALTER TABLE ... SWITCH PARTITION`) for near-instant
   archival movement.
2. Incremental statistics (`STATISTICS_INCREMENTAL = ON`) on partitioned
   indexes to reduce stats maintenance cost.
3. Partition-level index rebuild/reorg for hot partitions only.
4. Compression by age: `ROW` on warm partitions, `PAGE` on cold/archive.
5. Multi-filegroup tiering only if you need filegroup-level backup/restore or
   storage-tier separation; otherwise keep all partitions on `PRIMARY`.
