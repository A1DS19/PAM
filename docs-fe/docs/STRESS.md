# Stress

How we test the hot path under sustained load, what we found the first
time we did it, and the knobs that moved the needle. The methodology
lives in `tests/stress/README.md`; this doc captures the *findings* so
they survive past the conversation that produced them.

## Setup

- **Tool**: [k6](https://k6.io/), driven from the host.
- **Scripts**: `tests/stress/*.js`. Each scenario is its own file —
  fresh inserts, idempotent retries, validation rejects, bursts.
- **Environment**: `ASPNETCORE_ENVIRONMENT=Stress` flips three things:
  - Rate limiter off (no 429s polluting the throughput numbers).
  - Reconciler interval shortened to 1 min (so its behaviour is
    observable inside a 2-3 min run).
  - Discard consumers in `Pam.Notifications.Stress` bind a queue so
    RabbitMQ doesn't route-and-drop at the exchange.
- **Hardware reference baseline**: Apple Silicon laptop with SQL Server
  2022 under Rosetta. Real production hardware (native amd64) should
  comfortably do 3-10× these numbers.

Run sequence:

```bash
# Three terminals
make stress-up           # docker compose: mssql + rabbit + redis + otel-lgtm + seq
make stress-api          # API in Stress mode
make stress-reset        # wipe ingest + outbox + audit tables between runs
make stress-21g VUS=50 DURATION=2m
```

## The 21G fresh-insert scenario

A k6 ramp from 0 → N VUs over 30s, steady at N for `DURATION`, ramp
back to 0 over 20s. Each iteration POSTs `/v1/ingest/vendors/21g` with
a unique `Reference` so every request lands on the INSERT path (not
the duplicate-violation fast path). Result counters distinguish
accepted / duplicate / rate-limited / validation-fail / other-error.

## First-pass numbers, and what each tuning bought

Tracked against `vendor_transactions == dispatched_log == 89,456` (or
whatever the iter count was) — zero orphans is the must-have invariant
for every run.

| Run | VUs | Broker batch | Ingest audit interceptor | RPS | p50 | p95 | p99 | Notes |
|---|---|---|---|---|---|---|---|---|
| 1 | 50 | 100 (default) | on | 639 | 57 ms | 125 ms | 178 ms | baseline, all gates green |
| 2 | 100 | 100 (default) | on | 632 | 124 ms | 267 ms | 355 ms | RPS flat, latency 2×, p95 breached gate |
| 3 | 100 | **1000** | on | **800** | 98 ms | 200 ms | 268 ms | +27% RPS from broker tuning alone |
| 4 | 200 | 1000 | **off** | 712 | 235 ms | 412 ms | 529 ms | overload — past optimal concurrency |

Two surprising findings the runs surfaced:

### 1. SQL lock contention from the outbox delivery service was eating ~25% of foreground throughput.

Initial hypothesis after run #2: SQL Server is the wall, the broker is
keeping up cleanly (`outbox_message` drained to 0 after the run). That
diagnosis was wrong. The broker's `BusOutboxDeliveryService` runs
SELECT TOP / DELETE batches against `messaging.outbox_message` on its
own schedule, and at the default `MessageDeliveryLimit=100` it issued
many small batches per second — each one acquired locks on
`outbox_message` that competed with the foreground inserts.

Bumping to `MessageDeliveryLimit=1000` (10× larger batches, 10× fewer
operations) freed up the SQL lock budget. RPS climbed 632 → 800 and
p95 dropped 267 → 200 ms. The foreground path wasn't doing more work —
it was just waiting less.

Lesson: when "the DB is the bottleneck" stops being predictive, look
at what else is competing for DB locks. The outbox is async to the
request but synchronous to the database.

Knob in `Pam.Shared.Messaging/Extensions/MassTransitExtensions.cs`:

```csharp
o.UseBusOutbox(bo =>
{
    bo.MessageDeliveryLimit = 1000;
    bo.MessageDeliveryTimeout = TimeSpan.FromSeconds(30);
});
```

### 2. The optimal concurrency for this stack on local hardware is ~100 VUs.

Run #4 with VUs=200 produced LESS throughput than VUs=100 (712 vs
800 RPS) and p95 latency nearly doubled. Past a system's optimal
concurrency, more callers fight for the same resources — context
switches, SQL lock contention, pipeline-behavior dispatch — and net
throughput goes down.

Conventional wisdom for a JIT'd .NET service on local SQL: optimal
concurrency is roughly 2× the DB's CPU count, plus some headroom for
the async pipeline. On a real production DB instance the optimum will
be different — re-measure.

## Two correctness fixes the stress runs uncovered

Bugs that were latent under normal dev usage but surfaced under load.
Both are now fixed and pinned with tests.

### A. Cancellation cascade producing manufactured orphans

k6's connection-pool recycling and ramp-down stage triggers `HttpContext.RequestAborted`
mid-request. The CancellationToken propagated through MediatR pipeline behaviors
into EF Core's `SaveChangesAsync` — SQL Server cancelled the in-flight command,
which the exception handler logged as a 500.

Two parts to the fix:

1. **`OutboxFlushBehavior` no longer honours the request CT for the
   messaging commit.** Once COMMIT #1 (business row) succeeded, COMMIT #2
   (outbox + dispatched-log) must run unconditionally — otherwise a client
   disconnect in the microsecond gap between the two manufactures an
   orphan. Reconciler would catch it, but at the cost of unnecessary
   at-least-once republishes downstream.
2. **`CustomExceptionHandler` recognises client cancellation** —
   `OperationCanceledException` directly OR a `DbUpdateException` whose
   inner `SqlException` matches "Operation cancelled by user" when
   `HttpContext.RequestAborted` was the trigger. Logs at Debug, sets
   response code 499 (nginx convention for "client closed request"),
   skips the body write (the response stream is gone anyway).

After both fixes: zero 5xx during k6 ramp-down, zero manufactured
orphans. The remaining orphan source is genuine crashes between
COMMIT #1 and COMMIT #2 — caught by the reconciler on the next sweep.

### B. Reconciler scan budget caps orphan visibility

`IngestOutboxReconciler.ScanAndRepublishAsync` originally pulled every
business row in the lookback window into memory in batches, then diffed
against `dispatched_log` in-process. With `ScanBatchSize=2000 ×
MaxScanBatchesPerCycle=20`, each cycle covered at most 40k rows — and
the local keyset cursor reset every cycle, so the reconciler permanently
re-scanned the SAME first 40k rows.

Once a stress run filled the window past 40k, any orphans beyond row
40k became invisible. A 67-minute-old orphan that should have been
swept 65 times had never been touched.

Fix: replace the in-memory diff with a raw SQL `LEFT JOIN` against
`messaging.outbox_dispatched_log` returning ONLY orphan rows. The
per-cycle budget now caps the *orphan count* per call, not the *scan
depth*. Each successful republish moves rows out of the orphan set, so
the next iteration's query naturally returns the NEXT batch.

Regression pinned in `Pam.IntegrationTests.OutboxReconciliationTests.Reconciler_Republishes_Orphans_Past_Per_Cycle_Row_Budget`.

See ADR #29 for the full design rationale.

## Smaller tunings worth knowing

### Ingest audit interceptor skipped on the high-volume DbContext

`AuditableSaveChangesInterceptor` calls `IUserContext.Current` per save,
walking the HttpContext and resolving claims. For vendor traffic the
actor is always known up-front (the adapter authenticated the vendor),
so the per-save lookup is wasted work. We dropped the interceptor from
`IngestDbContext` and stamp audit columns inline in
`VendorTransaction.Record(...)` and `RecordRejected(...)`:

```csharp
tx.Stamp(receivedAt, new Actor(ActorType.Service, vendorId));
```

Two-for-one: skips the per-save reflection AND produces a more useful
audit trail (`Service:21g` instead of `Anonymous:anonymous`).

The interceptor stays attached to every other module's DbContext —
opting out is a per-aggregate decision based on (a) volume and (b)
whether the actor is known statically.

### `IngestTransactionCommand` skips `AuditBehavior` via `IUnauditedCommand`

Separate optimization from the previous one. `IUnauditedCommand` is a
marker on `ICommand` that makes `AuditBehavior` short-circuit, so no
`audit.command_log` row is written. At vendor-ingest volume the 1:1
row to `vendor_transactions` would bloat audit storage with no new
information — the business row already carries actor, payload, timing,
status. Failure capture for unaudited commands is still in
`LoggingBehavior` (Seq) + `OpenTelemetryBehavior` (span exception event).

### Stress-only discard consumers

Without consumers, RabbitMQ routes-and-drops at the integration-event
exchanges — no queue depth, no end-to-end observability. The
`Pam.Notifications.Stress.*` namespace contains no-op
`IConsumer<TransactionIngestedIntegrationEvent>` / `<BrandCreatedIntegrationEvent>`
that materialise the queue without contributing measurable work. Gated
by `Stress:DiscardConsumers:Enabled` so production never binds them.
The MT consumer-type filter in `AddPamMassTransit` is what enforces
the gating — the consumers exist in the same assembly as future real
consumers but never get registered outside stress runs.

## What to monitor while a run is in flight

Four panels you want open during a stress run:

| Where | What |
|---|---|
| The terminal running `make stress-21g` | k6 per-second VU count and complete-iteration count |
| Grafana Explore → Prometheus | `http_server_request_duration_seconds` p95, `http_server_active_requests` |
| RabbitMQ management UI (`http://localhost:15672`) | Publish rate vs deliver rate per exchange |
| `make stress-verify` (future) — currently the manual probe | `vendor_transactions == dispatched_log`, `outbox_message → 0` |

Post-run, ALWAYS run the row-count probe:

```bash
docker exec pam-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Pam_dev_password_123!' -No -I -d pam -Q "
  SELECT 'vendor_transactions' AS t, COUNT(*) AS rows FROM ingest.vendor_transactions
  UNION ALL SELECT 'dispatched_log', COUNT(*) FROM messaging.outbox_dispatched_log
  UNION ALL SELECT 'outbox_message', COUNT(*) FROM messaging.outbox_message;"
```

`vendor_transactions == dispatched_log` is the must-have invariant.
Any delta is either inside the reconciler's `MinAge` grace (last 30s
of the run, in Stress mode) or a real defect.

## Next levers (untested as of this writing)

1. **Run the API on Linux natively** (not the SQL container — the
   API). Removes Rosetta from the .NET process. Likely +3-5× on the
   .NET-side ceiling, leaves the SQL side unchanged.
2. **Pre-warm the JIT.** First 30s of a run includes tier-0 JIT
   warmup; later iterations are tier-1 + tiered PGO. Add a warm-up
   stage to the k6 script that doesn't count toward percentiles.
3. **Tune `BusOutboxDeliveryService` further.** `MessageDeliveryLimit=1000`
   helped; 2000-5000 might extend the window before broker-side lag
   shows up.
4. **Move ingest writes to a separate connection pool.** EF Core can
   be configured with a dedicated `SqlConnection` factory for the
   ingest DbContext, so the reconciler scans don't compete for the
   same connections.

Each one is a separate experiment with a separate measurement; resist
the temptation to land them as a single PR.
