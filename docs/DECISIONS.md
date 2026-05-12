# Decisions

A log of architectural decisions made during the project's life. Each entry
captures the **context** (why the choice came up), the **decision**, and the
**consequences** (what we accept by choosing this). Entries are
append-only — supersede rather than rewrite when a decision changes.

Rough ADR shape, kept terse. Order is reverse-chronological.

---

## 27. Database platform: SQL Server (supersedes ADR #22) — 2026-05-12

**Status:** supersedes ADR #22 ("Postgres over MS SQL Server").

**Context.** ADR #22 picked Postgres on technical grounds (jsonb, partial
indexes, exclusion constraints, `timestamptz` correctness, snake_case
ergonomics, `pg_trgm` for searching player names, no vendor lock).
That decision held until the 2026-05-12 stakeholder meeting, which
revisited the choice for non-technical reasons:

- **Operational expertise.** The DBA bench at the company is built
  around SQL Server. Every production DB they currently operate is SQL
  Server; they do not have Postgres on-call rotation, runbooks,
  backup/restore playbooks, or DR rehearsal procedures. Adding Postgres
  would mean either (a) building that capability from scratch
  alongside everything else PAM is shipping, or (b) shipping PAM
  without the operational depth the rest of the platform has.
- **Existing licensed infrastructure.** SQL Server licenses, the
  monitoring stack pointed at them, the BAC backup destination, and
  the SAN provisioning patterns are all already in place. Postgres
  would have required net-new provisioning across all four.
- **CS-facing tooling reuses the same DB.** Per the meeting, Rodolfo +
  Milton's CS site will be built against the same database PAM writes
  to. That team is also a SQL Server shop.

ADR #22's technical advantages (jsonb, partial indexes, etc.) are
real, but PAM did not yet rely on any of them — the codebase used
plain relational columns + EF Core's provider-neutral abstractions
throughout. The cost of switching at this point is bounded: change
the providers, regenerate migrations, swap the docker-compose image,
re-run the smoke tests.

**Decision.** PAM moves to **SQL Server 2022** for development, QA,
and production. Local dev uses
`mcr.microsoft.com/mssql/server:2022-latest` via Docker (with
`platform: linux/amd64` for Apple Silicon hosts running under Rosetta
emulation; QA + prod run native amd64). QA infra is being provisioned
by IT per the same meeting.

Provider-specific scope of work (executed 2026-05-12):

- `Directory.Packages.props`: drop `Npgsql.EntityFrameworkCore.PostgreSQL`,
  `Npgsql.OpenTelemetry`, `AspNetCore.HealthChecks.NpgSql`,
  `Testcontainers.PostgreSql`; add
  `Microsoft.EntityFrameworkCore.SqlServer`,
  `AspNetCore.HealthChecks.SqlServer`, `Testcontainers.MsSql`.
- All module `AddXModule`: `UseNpgsql(...)` → `UseSqlServer(...)`.
- All design-time factories: same swap.
- `Pam.Shared.Messaging.AddPamMassTransit`: `o.UsePostgres()` →
  `o.UseSqlServer()` for the MT outbox.
- Idempotency error code: `PostgresException.SqlState == "23505"` →
  `SqlException.Number == 2601 || 2627` for unique-violation detection
  in `IngestTransactionHandler`.
- All seven `Data/Migrations/` folders deleted and regenerated against
  the SQL Server provider (Postgres migrations encode provider-specific
  column types like `timestamptz`/`jsonb` that don't exist on SQL Server).
- `docker-compose.yml`: `postgres:17` → `mcr.microsoft.com/mssql/server:2022-latest`.
- `ConnectionStrings:Pam`: `Host=localhost;Port=5432;...` →
  `Server=localhost,1433;Database=pam;User Id=sa;Password=...;TrustServerCertificate=True;Encrypt=False`.
- `Testcontainers.PostgreSql` → `Testcontainers.MsSql` in integration
  test fixtures.

**Preserved (no change):**

- `EFCore.NamingConventions` snake_case stays. The package supports
  both providers; the DBA team has reviewed the convention and is
  fine with it.
- `DateTimeOffset` → `datetimeoffset(7)` on SQL Server (analogous to
  Postgres `timestamptz`). The `IClock.UtcNow` rule and the banned
  symbols on `DateTime.UtcNow` stay.
- `bigint` signed cents for money — same on both providers.
- The single-shared `PamMessagingDbContext` outbox topology from ADR
  #26 — MT supports SQL Server outbox via `o.UseSqlServer()`.

**Consequences.**

- The technical wins from ADR #22 (jsonb, partial indexes, exclusion
  constraints, pg_trgm search) are off the table. PAM was not yet
  using any of them; future requirements that genuinely need them
  become "request a Postgres-on-the-side" conversations with IT, not
  refactors. JSON payload storage falls back to `nvarchar(max)` with
  `OPENJSON` for query, which is good enough for the audit log's
  redacted-JSON column.
- `Testcontainers.MsSql` boot time is significantly slower than
  `Testcontainers.PostgreSql` on Apple Silicon (Rosetta emulation).
  CI on amd64 is unaffected. Local integration tests slow down by
  ~20-30 s per test class fixture; acceptable, document in TESTING.md.
- Provider-coupled error codes need explicit handling — the
  `SQLSTATE 23505` pattern that catches concurrent-retry unique
  violations in `IngestTransactionHandler` becomes a SQL Server
  error-number check. Helper extension method to abstract.
- The QA cutover is unblocked. IT can provision a SQL Server instance
  and the same `dotnet ef database update` pipeline applies.

See [LOCAL_DEV.md](LOCAL_DEV.md) for the updated connection-string +
docker-compose shape, [INGEST.md](INGEST.md) for the refreshed
smoke-test commands using `sqlcmd` instead of `psql`, and ADR #22 as
the historical record of the original (now reversed) call.

---

## 26. Single shared `PamMessagingDbContext` owns the MT outbox — 2026-05-12

**Context.** The 2026-05-12 SOAP smoke test for the 21G listener
succeeded end-to-end (row in `ingest.vendor_transactions`, audit row
captured, duplicate replay returned `Duplicate`) but produced **zero
outbox rows and zero PAM exchanges in RabbitMQ**. Diagnosis revealed a
structural bug in the per-module outbox topology PAM had inherited
from the initial scaffold.

What we had:

- Each publishing module (Operators, Ingest, Wallet) called
  `AddEntityFrameworkOutbox<TDbContext>(...)` against its own
  DbContext, with the inbox/outbox tables living in its own schema
  (`operators.outbox_message`, `ingest.outbox_message`, etc.).
- `OperatorsModule.ConfigureOutbox` was the single caller of
  `.UseBusOutbox()`, on the theory that one global publish-intercepting
  filter would route all publishes to the active save's outbox table.

Why that didn't work, verified by direct reflection on
`MassTransit.EntityFrameworkCoreIntegration` 8.5.9 and confirmed by the
MassTransit author in
[discussion #5480](https://github.com/MassTransit/MassTransit/discussions/5480):

- `UseBusOutbox()` swaps the scoped `IScopedBusContextProvider<IBus>`
  with `EntityFrameworkScopedBusContextProvider<IBus, TDbContext>`.
  That registration is keyed on the bus type alone — there is only
  ever one `IScopedBusContextProvider<IBus>` per bus per scope.
- The provider is bound to ONE DbContext. Every `IPublishEndpoint.Publish`
  in scope writes the `OutboxMessage` row into that one DbContext's
  change tracker.
- The SOAP request path saves `IngestDbContext` but never saves
  `OperatorsDbContext` — so MT writes the outbox row to a change tracker
  that is then garbage-collected when the scope ends.
- The multi-DbContext outbox overload `AddEntityFrameworkOutbox<TBus, TDbContext>`
  arrived in MT 9.1, which is on the commercial Massient license and
  off-limits while we hold the Apache-2.0 line (ADR #5).

**Decision.** A single `PamMessagingDbContext` (schema `messaging`) owns
the MT inbox/outbox tables. `AddPamMassTransit` (in
`Pam.Shared.Messaging`) is the only place `UseBusOutbox()` is called,
on this one context. No per-module `ConfigureOutbox` hooks. Module
DbContexts no longer carry `AddInboxStateEntity`/`AddOutboxMessageEntity`/
`AddOutboxStateEntity`; a `RemoveOutboxTables` migration in each module
drops the now-orphaned per-module tables.

Bridge handlers continue to call `IPublishEndpoint.Publish` unchanged.
The bus-wide outbox routes each call's `OutboxMessage` row into
`PamMessagingDbContext`'s change tracker. An `OutboxFlushBehavior`
MediatR pipeline behavior (registered as innermost, after `AuditBehavior`)
calls `messaging.SaveChangesAsync` at the tail of every command — that
commits the queued outbox rows; `BusOutboxNotification` immediately
wakes `BusOutboxDeliveryService<PamMessagingDbContext>`, which publishes
to RabbitMQ and removes the delivered rows. End-to-end latency in the
local smoke test is sub-second.

**Atomicity caveat (accepted, time-bounded).** The business `SaveChanges`
(module DbContext) and the outbox `SaveChanges` (messaging DbContext)
run in **separate transactions** at present. A process crash between
the business commit and the outbox commit leaves the business row
persisted but the integration event undelivered — an at-most-once
failure on the specific request, recoverable only by reconciliation.

We pick this trade-off over the alternatives because:

- The MT author's
  [endorsed pattern](https://github.com/MassTransit/MassTransit/discussions/5480)
  for 8.5.x multi-module monoliths is exactly this shared-context
  shape; the upgrade to true cross-context atomicity is a known
  follow-up.
- The textbook fix — sharing one `NpgsqlConnection` + one
  `IDbContextTransaction` between business and messaging DbContexts —
  is real but invasive: every `AddDbContext<T>` registration must resolve
  the ambient connection from a scoped service, a MediatR pipeline
  behavior must `BeginTransactionAsync` before any DbContext queries,
  and each business DbContext must `UseTransactionAsync` on first save.
  EF's `AutoTransactionBehavior` would need to be overridden. Doable;
  not today's priority.
- Custom `IScopedBusContextProvider<IBus>` routing (resolve the right
  per-DbContext provider based on which save is currently active) was
  evaluated and rejected: phatboyg has called this "custom development"
  not supported by MT, the interface is undocumented in 8.5, and the
  routing must answer "which DbContext is in scope" at *publish time*
  via an `AsyncLocal` set from the dispatch interceptor. Fragile.
- MultiBus does not help — the outbox is `IBus`-only pre-9.1 per
  [discussion #4435](https://github.com/MassTransit/MassTransit/discussions/4435).

Follow-ups (logged in [ROADMAP.md](ROADMAP.md)):

- Implement shared-connection-shared-transaction atomicity, OR
- Migrate to MT 9.1 per-DbContext outbox once the licensing question
  is resolved.
- A reconciliation job that diff-checks business rows against
  delivered events lands as a defensive backstop regardless of which
  path above we pick.

**Consequences.**

- One delivery service (`BusOutboxDeliveryService<PamMessagingDbContext>`)
  instead of three. One Postgres advisory-lock loop. Cross-module
  message ordering is now globally consistent without coordination.
- Adding a new publishing module = add a domain event + bridge handler,
  done. No outbox plumbing per module.
- The `messaging` schema is touched by every command pipeline that
  publishes. It's a write-hot table. Monitor accordingly.
- We carry an under-deliver risk until atomicity work lands — bounded
  by request-tail-time (~ms) and recoverable via reconciliation.
- `MassTransit.EntityFrameworkCore` is now referenced only by
  `Pam.Shared.Messaging`. Module csprojs lost the package reference.
- The smoke test's verification commands are documented in
  [INGEST.md](INGEST.md) "Phase A SOAP listener" section, including
  the "outbox in steady state is empty (rows are removed after
  delivery)" gotcha that tripped me up during initial verification.

See [INGEST.md](INGEST.md) for the verified smoke-test commands,
[ARCHITECTURE.md](ARCHITECTURE.md) "Outbox + pre-save domain-event
dispatch" for the publish pipeline, and `Pam.Shared.Messaging/` for
the implementation.

---

## 25. `Pam.Ingest` — vendor-adapter pattern + four-phase GBS strangler — 2026-05-12

**Context.** The CTO doc's CS Part 1 ("Unified Transaction View")
requires intercepting third-party transactions from casinos, lottos,
3rd-party sportsbooks, horses, and the cashier — normalising them and
making them queryable in one place. Exploration of the legacy GBS code
(`~/Desktop/work/gbs/`) showed that GBS already has the right *data*
model (one shared `tbCasinoPlayToday` table per vendor, identified by
`SystemID`, with `Reference` as the idempotency key) but a broken
*integration* model: one bespoke controller per vendor (SOAP for 21G,
REST+HMAC-MD5 for BTCasino, etc.), float-cents money columns, and
`DATETIME` without timezone — root cause of the Databricks
reconciliation mismatch Roger flagged.

PAM needs to (a) absorb every vendor through a single canonical write
path, (b) coexist with GBS during a long dual-running window, and (c)
not bake assumptions about wallet authority into the ingest layer
(`Pam.Wallet` ships later, after CS Part 1 / Players).

**Decision.**

- **A new `Pam.Ingest` module** with one canonical aggregate
  (`VendorTransaction`) writing to `ingest.vendor_transactions`. One
  table for every vendor; `vendor_id` is the discriminator. Mirrors
  GBS's clean shared-table choice.

- **`IVendorAdapter` per vendor.** Each adapter owns its vendor's auth
  (HMAC, plaintext password, bearer, IP allow-list), payload parsing,
  and response shape. The endpoint is a five-line Carter route that
  calls `Authenticate → Translate → Send → FormatResponse`. Adding a
  new vendor is mechanical.

- **Canonical fixes vs GBS**:
  - `bigint` signed cents (NOT float). Risk is negative, Win positive.
    Adapter applies sign per vendor convention.
  - `DateTimeOffset` → `timestamptz` everywhere. Two distinct stamps:
    vendor-reported `OccurredAt` and PAM-recorded `ReceivedAt`.
  - `UNIQUE (vendor_id, vendor_reference)` index enforces idempotency.
    Handler catches `PostgresErrorCodes.UniqueViolation` for the
    concurrent-retry race and surfaces `TransactionStatus.Duplicate`.
  - `TransactionStatus` enum (`Received | Posted | Duplicate |
    Rejected`) separates lifecycle state from storage. GBS's
    "non-posted vs posted" UX collapses into status transitions.

- **Outbox-from-day-one.** Bridge handler
  (`TransactionIngestedDomainHandler`) calls `IPublishEndpoint.Publish`;
  the bus-wide outbox in `Pam.Shared.Messaging` (ADR #26) routes the
  message into `PamMessagingDbContext`, which the `OutboxFlushBehavior`
  pipeline commits at the tail of the command. The delivery service
  forwards to RabbitMQ. See ADR #26 for the atomicity trade-off
  currently accepted between business and outbox commits.

- **Four-phase strangler migration**, deliberately shippable
  independently per vendor:
  1. **Phase A — Intercept-and-forward.** PAM persists, then proxies
     the original request to GBS. Zero functional change.
  2. **Phase B — Emit integration events.** Downstream consumers
     (Reporting, Notifications, Audit) start landing.
  3. **Phase C — PAM authoritative for one vendor.** PAM stops
     forwarding for that vendor; either posts to GBS's wallet via
     existing stored proc, or (Phase C') through to `Pam.Wallet`. A
     sync job keeps `tbCasinoPlayToday` populated so Crystal Reports
     keep working.
  4. **Phase D — Listo.** GBS casino write path retired;
     `tbCasinoPlayToday` becomes a read-only view fed from
     `ingest.vendor_transactions`.

- **Route convention**: `POST /v1/ingest/vendors/{vendor-code}`.
  Anonymous + rate-limited via `api-default`. The adapter owns vendor
  auth; PAM JWT is irrelevant on this surface.

**Consequences.**

- Adding a casino vendor is implementing one adapter (~150 LOC) and
  registering it. No schema changes, no migration, no new tests beyond
  the adapter's translation logic.
- The unified-transaction-view query is a single indexed read against
  `ingest.vendor_transactions` filtered by `(brand_id, player_id)`.
  This is what CS Part 1's customer-management screen calls.
- `Pam.Players` is on the critical path: the adapter needs
  `IPlayerLookup` from `Pam.Players.Contracts` to resolve vendor
  usernames to `PlayerId`. Until Players ships, vendor adapters
  accept the resolved PlayerId from a stub mapping.
- A vendor can be at Phase A while another is at Phase C — phases are
  per-vendor, not module-wide.
- `Pam.Wallet` doesn't have to exist for Phase A / B value. Phase A
  starts as soon as one real vendor adapter is wired.
- Crystal Reports continuity is explicit (Phase D keeps a view called
  `tbCasinoPlayToday`). Existing GBS-side reports don't need to be
  rewritten as part of the migration.
- Skipped: streaming-from-day-one (Kafka). The integration-event
  outbox over RabbitMQ covers Phase A/B. ADR #24 covers when (and how)
  to layer Kafka in for the high-throughput transaction stream.

See [INGEST.md](INGEST.md) for the module reference,
[ARCHITECTURE.md](ARCHITECTURE.md)'s outbox section for the publish
semantics, and the GBS exploration brief retained in chat history for
the source-of-truth read on what GBS actually does today.

---

## 24. RabbitMQ for integration events; defer streaming substrate to CS Part 1 — 2026-05-12

**Context.** The CTO evaluation doc (`~/Downloads/BA Core Platform
Evaluation Process.pdf`) listed both "Streaming?: Kafka?" and the
broader messaging question as open. We picked RabbitMQ via MassTransit
for cross-module events without a written rationale. Roger's CS Part 1
requirement — "store stream of GBS Sportsbook updates and GBS other
updates" plus intercepting casino / lotto / 3rd-party SB / horses /
cashier transactions — is structurally **append-only, ordered,
replayable from offsets**, which is Kafka-shaped, not queue-shaped.
Both patterns will eventually exist; the decision is what to pick now
versus defer.

**Decision.**

- **RabbitMQ stays the integration-event substrate** indefinitely. The
  pub-sub model fits "thing happened, who cares?" — fan-out broadcast,
  ephemeral queues per consumer, MassTransit's redelivery for retries,
  EF Core outbox for transactional publishing. The whole `Pam.<Module>.
  Contracts/IntegrationEvents` pattern is built on it.

- **Streaming substrate is deferred to CS Part 1.** When the
  transaction-intercept module starts, pick between:
  - **Kafka** (Confluent / Strimzi / Redpanda) — textbook answer,
    mature tooling, real operational weight.
  - **NATS JetStream** — single-binary, append-only streams with
    offsets, lighter ops, smaller ecosystem.
  - **Postgres logical replication + a stream consumer** — uses the
    DB we already have; cheaper; no replay-from-arbitrary-offset.
  That comparison happens during CS Part 1 design, not today.

- **MassTransit supports a Kafka rider**, so the cross-module
  integration-event seam stays unchanged even when Kafka lands for the
  ingest stream. Two transports, one façade.

**Consequences.**
- One broker today; two when CS Part 1 ships. No over-investment in
  Kafka before we have a workload that justifies its operational
  weight.
- The trigger to revisit is explicit: first PR of `Pam.Ingest` (or
  whatever the transaction-intercept module ends up named).
- We don't pretend RabbitMQ can do "store-and-replay-from-offset" — we
  document it can't, and accept the second dependency when we need it.

---

## 23. .NET / C# over Go, Rust, JavaScript for the back-office — 2026-05-12

**Context.** The CTO evaluation doc listed "Backend languages: Go,
Rust, C#, JavaScript?" with a question mark. The team is already
building on .NET 10. The decision needs to be written so the formal
June 1 evaluation has a rationale on file, and so the next time someone
asks "why didn't we pick Go" the answer isn't an oral tradition.

**Decision.** **.NET 10 / C#** for the back-office monolith and any
microservice extracted from it.

Front-end stays TypeScript (React + TanStack Start) — polyglot at the
HTTP edge is fine and intentional. Future live-betting / in-play
engine is **its own decision in its own service** — that's where Go,
Elixir, or Rust might earn their keep, but it's not the back-office's
question.

Why .NET wins this specific workload:

- **Domain modeling fit.** Regulated finance + iGaming needs rich
  aggregates with invariants (Player, Wallet, Limits, Bonuses). C#
  records + sealed hierarchies + exhaustive `switch` + a strong
  analyzer ecosystem (`Meziantou.Analyzer`,
  `Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`,
  `BannedApiAnalyzers`) fit DDD well. Go's lack of sum types and
  Rust's productivity floor both penalize this pattern.
- **Back-office ecosystem.** EF Core, ASP.NET Core Identity,
  OpenIddict, MassTransit, MediatR, FluentValidation, OpenTelemetry,
  Quartz — every layer of the platform is one mainstream library
  away. JVM matches; Go is thinner; Rust is thinner still for
  line-of-business work.
- **Hiring pool in LATAM.** The team is .NET-fluent; nearshore .NET
  supply is large. Pivoting to Go means retraining or rehiring;
  pivoting to Rust means both.
- **Peer project (`pro-cr-billing`) is .NET 10.** Patterns, ops
  knowledge, and SAST/CI tooling transfer.
- **No performance ceiling we'd hit.** Back-office traffic is
  hundreds of req/s, not tens of thousands. .NET's GC + AOT story is
  more than sufficient. The "Go is faster" argument only kicks in at
  high-concurrency / low-latency hot paths, which are a separate
  service when (or if) they ship.

**Consequences.**
- Zero language license cost (.NET is MIT/Apache-2.0). The license
  risks already absorbed (MediatR 12.4.1, MassTransit 8.5.9,
  FluentAssertions 7.2.0 — ADR #5) are documented.
- Front-end / back-end language split is intentional. Carter +
  Scalar generate an OpenAPI spec the TS client can consume directly.
- Future live-betting service is unconstrained by this decision —
  that future module can be Go, Elixir, or another .NET service. The
  contracts assembly + RabbitMQ exchange shapes are language-neutral.
- Re-evaluating this is expensive once we have N modules; the right
  time to revisit was the start of the project, which is now.

---

## 22. Postgres over MS SQL Server — 2026-05-12 (SUPERSEDED by ADR #27, same day)

> **Superseded later the same day** by [ADR #27](#27-database-platform-sql-server-supersedes-adr-22--2026-05-12). The stakeholder meeting flipped the call to SQL Server on
> operational grounds (DBA expertise + existing licensed infra). The
> historical reasoning below is preserved as the record of why we
> initially chose Postgres on technical grounds — none of those points
> were wrong, they just weren't load-bearing once operational realities
> entered the conversation.

**Context.** GBS runs on MS SQL Server. The CTO evaluation doc listed
"DB: MS SQL Server?" with a question mark. We picked Postgres 17 from
the start of this project. The pick has friction (the GBS migration
crosses RDBMS vendors, not just schemas) and benefit (modern Postgres
features fit the patterns we're using). The choice needs to be on the
record before the June 1 decision.

**Decision.** **Postgres 17** for every PAM module.

Reasons that compound:

- **JSONB.** `audit.command_log.payload_json` is jsonb — searchable
  from psql/Grafana with `payload_json -> 'field'`, indexable on
  individual paths. The future regulatory-audit module (`Pam.Audits`)
  is built on this. SQL Server's JSON support exists but isn't
  indexable the same way.
- **Advisory locks.** `pg_advisory_xact_lock(100_001)` wraps the
  concurrent-boot seeder (ADR-equivalent in `PLATFORM_HARDENING.md`).
  Future distributed locks for periodic jobs use the same pattern.
  SQL Server's `sp_getapplock` works but its semantics are messier.
- **Partitioning model.** Time-based partitioning on
  `audit.command_log`, future `wallet.ledger_entry`, and the
  not-yet-built CS Part 1 transaction stream is straightforward in
  Postgres (declarative partitions, attach/detach). SQL Server's
  partitioning is more ceremonial (partition functions + schemes +
  filegroups).
- **Snake_case idiom matched by `EFCore.NamingConventions`** (ADR
  #18). `psql` and `pgcli` read naturally; SQL Server tooling
  conventions are PascalCase by default.
- **License + ops cost.** Postgres is free; Npgsql is mature.
  SQL Server licensing scales with cores (Enterprise) or is feature-
  limited (Standard). Operational weight is lighter — single binary,
  no SQL Agent jobs to recreate (we use Quartz).
- **Peer project alignment.** `pro-cr-billing` runs Postgres; the
  team is already conversant; SAST/CI patterns transfer.
- **Multi-replica primitives we already use** (DP keyring,
  OpenIddict authorization storage, EF outbox) work identically on
  Postgres and SQL Server, but our exact Npgsql tooling chain is
  already wired.

**Consequences.**
- Migration from `gbs-db` requires a one-off SQL-Server-to-Postgres
  data pump per table, or a CDC tool (Debezium, Fivetran, custom). The
  357 tables / 1,901 stored procs need normalization regardless of
  target DB; this is a one-time cost paid during the strangler-fig
  migration, not an ongoing one.
- All EF Core code uses `Npgsql.EntityFrameworkCore.PostgreSQL`. The
  module pattern + design-time factory + `UseSnakeCaseNamingConvention()`
  is consistent and copyable.
- Drift from GBS during dual-running is unavoidable. The strangler-fig
  design (deferred per ROADMAP) covers reconciliation between the two
  stores during the cutover window.
- Reverting to SQL Server gets expensive once Players + Wallet hold
  real data — make the pick now, before that.
- The choice does **not** preclude a SQL-Server-shaped read-replica
  for legacy reporting tools (Crystal Reports, Databricks ingestion
  during the transition). That's a separate question handled by the
  CS Part 1 + financial-reporting work.

---

## 21. Attribute-driven Redis caching as a MediatR behavior — 2026-05-11

**Context.** A back-office hits `GetUser` and `ListUsers` on every page
load; the underlying Postgres reads aren't expensive yet but the
pattern repeats for every module we'll add. We already have a shared
Redis multiplexer (registered as singleton for the rate-limiter and
DP-keys) and the MediatR pipeline (`LoggingBehavior`,
`ValidationBehavior`). The two natural ways to plug caching in are
(a) per-handler — each handler asks `IMemoryCache`/`IDistributedCache`
directly, or (b) a pipeline behavior gated by attributes on the
request type.

**Decision.** Behavior with attributes. A query opts in with
`[Cache(durationMinutes, keyPattern)]`; a command opts in with
`[InvalidateCache(...patterns)]`. The behavior reads
`typeof(TRequest).GetCustomAttribute<...>()` once per type and short-
circuits on hit. `ICacheService` lives in `Pam.Shared.Contracts`;
`RedisCacheService` (the only implementation) lives in `Pam.Shared`.
Pipeline order is `Logging → Validation → Caching → handler`.

Three sub-decisions worth pinning:

- **Redis-only, no in-memory fallback.** `Program.cs` already throws at
  boot if `ConnectionStrings:Redis` is missing — the whole platform
  assumes a shared multiplexer. A second code path that silently falls
  back to in-memory caching would drift from prod and bite us in a
  multi-replica deploy where two API pods would each cache + invalidate
  in their own process. Skip the option.
- **Direct on `IConnectionMultiplexer`, not `IDistributedCache`.** We
  need `IServer.KeysAsync` for pattern-based invalidation (the SCAN
  cursor); `IDistributedCache` doesn't expose it. Going one layer down
  also matches how the rate-limiter and DP-keys consume the
  multiplexer, so we keep a single Redis seam.
- **Key prefix `pam:cache:`.** Segregates from the rate-limiter
  (`pam-api-rl:*`) and any future Redis use (locks, sessions). Pattern
  delete never spills out of the cache namespace.

**Consequences.**
- A new module turns on caching with two attributes; no DI wiring per
  feature. Cache lives in one place — easy to disable globally if
  needed.
- `[InvalidateCache]` patterns are literal — no `{Prop}` interpolation
  against the command — so today's mutations purge the whole
  `identity:user:*` namespace. Bounded but coarser than ideal.
  Interpolation can be added later by routing each pattern through
  `CacheKeyGenerator.GenerateKey(request, pattern)` inside the
  behavior; deferred until the broad purge shows up as a cost.
- Cached payloads are JSON. A renamed DTO field deserializes with the
  old shape until TTL expires. Keep TTLs short and bump `keyPattern`
  on shape changes.
- No stampede protection. Multiple concurrent misses each run the
  handler. Acceptable now; revisit when a hot key appears.

See `docs/CACHING.md` for usage and gotchas.

---

## 20. Roles + Permissions for back-office authz, not roles only — 2026-05-07

**Context.** The peer `pro-cr-billing` project uses three role-based
policies (Owner / Accountant / Operator) with no permission layer. The
PAM operator manual ("Employee Access and Security") and the legacy
`IqSoft.CP.BLL.PermissionBll` both demand fine-grained per-action
permissions. Retrofitting permissions across endpoints once dozens are
in place is painful.

**Decision.** Phase 3 `Pam.Identity` ships with both:
- `IdentityRole<Guid>` (Owner / Manager / Operator / Accountant — coarse
  buckets, seeded at startup).
- A custom `Permission` + `RolePermission` join (well-known string codes
  like `operators.read`, `players.write`).

Both project as claims at token-issuance time via `SetDestinations`.
`[Authorize(Policy = "Permissions.Players.Read")]` and `RequireRole(...)`
are both supported; choose the granularity each endpoint actually needs.

**Consequences.**
- Slightly more code at module start. Big payoff when a customer-support
  team needs to do X but not Y.
- Permission codes live as constants in `Pam.Identity.Contracts`; other
  modules reference those constants (no string typos).

---

## 19. Per-module 3-project pattern with Add+Use extension methods — 2026-05-07

**Context.** Without a written rule, module shape drifts. Some modules
might grow Application/Domain/Infrastructure layers, others go vertical
slice, others mix. Future readers and `ultrareview`-style architecture
checks need a single template.

**Decision.** Every module ships as exactly three projects:
- `Pam.<Module>.Contracts` — DTOs, `IQuery<T>`, integration events.
- `Pam.<Module>` — aggregates, vertical-slice features, EF, wire-up.
- `tests/Pam.<Module>.UnitTests` — aggregate + validator unit tests.

Wire-up via a static `<Module>Module` class exposing
`AddXModule(IServiceCollection, IConfiguration)` and
`UseXModuleAsync(IServiceProvider)`. Schema-per-module + snake_case
naming + explicit health probe. Interceptors registered per-module via
`TryAddScoped` so multiple modules don't conflict.

**Consequences.**
- New-module cost is low; copy `Pam.Operators` and rename.
- `NetArchTest.Rules` (pinned in catalog, wiring deferred) can enforce
  the boundary contract once a second module exists.
- `Pam.Operators` is the canonical reference. Diverging from this
  pattern requires a new ADR.

---

## 18. Snake_case columns via EFCore.NamingConventions — 2026-05-07

**Context.** EF Core defaults to PascalCase column names matching C#
property names. PostgreSQL idiom is snake_case; the peer
`pro-cr-billing` project uses snake_case throughout. Inconsistency
between case styles in the same project is a smell, and retrofitting
once N modules ship is N migrations.

**Decision.** Add `EFCore.NamingConventions` 10.0.0 to the central
catalog. Every module DbContext applies
`options.UseSnakeCaseNamingConvention()` on **both** the runtime DI
registration and the design-time factory. Without it on the design-time
side, `dotnet ef migrations add` scaffolds PascalCase column names that
disagree with runtime queries.

**Consequences.**
- DB tables read naturally from `psql`. Column names match Postgres
  conventions and tooling.
- Established before Phase 3 to avoid renaming Identity + OpenIddict
  schemas later.

---

## 17. Operators-first module ordering; Players deferred — 2026-05-06

**Context.** The original sequencing was Players first (with a hardcoded
`IBrandRegistry`), then Operators promotion, then KYC/Wallet/etc. After
the ZITADEL drop (#16), the dependency graph looked wrong:
back-office Identity is needed before any module can be admin-managed,
and Brand is the multi-tenant foundation that every other aggregate
references. Putting Players first meant baking in a brand abstraction
layer twice (once as a hardcoded shim, once as the real thing) and
rewriting the existing Player-Brand wiring later.

**Decision.** New module sequence: **Operators → Identity → Players →
Wallet → KYC → Limits → Bonuses → Bets → Affiliates → Notifications →
Reporting → Audit**. Phase 1 deleted the existing `Pam.Players` module
and its tests entirely; Phase 2 shipped `Pam.Operators` with the
`Brand` aggregate; Phase 3 is `Pam.Identity` (back-office Users &
Agents). Players gets re-introduced as the fourth module, now properly
scoped under a `Brand` and authenticated via the embedded IDP.

**Consequences.**
- ~2200 lines of Players + ZITADEL scaffolding deleted (commit
  `2a90022`); not lost — every pattern worth keeping was either already
  in `Pam.Shared` or is being rebuilt cleanly.
- Memory file `project_phase1_pivot.md` captures the rationale and the
  patterns the next phase inherits.

---

## 16. Drop ZITADEL; embed OpenIddict + ASP.NET Core Identity — 2026-05-06

**Context.** Decisions #4 and #12 picked Keycloak then ZITADEL as the
external IDP. The company's stronger requirement is sovereignty over
the auth code path — "we want to own the entire codebase." External
IDPs introduce: (a) a separate process to operate, (b) a second DB to
back up and migrate, (c) a release cadence we don't control, (d)
cross-process domain leakage (Brand↔ZITADEL Org sync).

**Decision.** Replace ZITADEL with **OpenIddict + ASP.NET Core
Identity**, both embedded as NuGet packages in `Pam.Identity` (Phase 3).
- OpenIddict (MIT) issues OAuth 2.0 / OIDC tokens. Authorization Code +
  PKCE + Refresh for the back-office SPA; client_credentials
  pre-enabled for future module extraction.
- ASP.NET Core Identity owns user storage, password hashing, lockout,
  email confirmation, MFA. Both stacks share `IdentityDbContext` in
  schema `identity`.
- `BackOfficeUser : IdentityUser<Guid>` carries `BrandId`. Player
  authentication is deferred to when the Players module returns; design
  intent is a distinct flow within the same OpenIddict server (likely a
  separate scope set).
- Phase 1 deletes everything ZITADEL: `infra/zitadel/`, the three
  ZITADEL services from `docker-compose.yml`, all `Zitadel.*` package
  refs, the `IIdentityProvider` ZITADEL implementation,
  `BrandRegistry`, `IBrandRegistry`, and the related Players-module
  scaffolding. Phase 3 brings auth back via OpenIddict.

**Consequences.**
- Single process, single DB, MIT/Apache-2.0 source we ship in our
  binary. If the maintainer disappears we keep building.
- More code we own (custom `/connect/authorize` + `/connect/token`
  controllers; permissions seeder; cert rotation). Bounded; documented
  in `project_phase1_pivot.md`.
- Supersedes #4, #12, #15.

---

## 15. ZITADEL bootstrap as `IHostedService`, not a shell script — 2026-05-06

**Status: superseded by #16.**



**Context.** Decision #12 introduced ZITADEL with a bash + curl + python3
bootstrap script that read the admin PAT from a Docker volume, then made
three API calls to ensure Orgs and Project. The script kept hitting
portability issues: volume-path lookup differs per Docker setup, ZITADEL
changed the org-search endpoint between versions, the chicken-and-egg
PAT extraction needed permission fix-ups, and idempotency-check fallbacks
were noisy on fresh runs.

**Decision.** Remove `infra/zitadel/bootstrap.sh` and `.env.zitadel`.
Bootstrap moves into `Pam.Api` as `ZitadelBootstrapService : IHostedService`,
which:
- waits for ZITADEL's OIDC discovery endpoint to come online (2-min cap),
- reads the PAT from a configurable file path (default
  `infra/zitadel/machinekey/zitadel-admin-sa.pat`, walks up to repo root
  if invoked from a sub-dir),
- ensures the configured Brand Orgs exist (gRPC `OrganizationService.AddOrganization`
  via the smartive `Zitadel` SDK; on AlreadyExists falls back to `ListOrganizations`),
- ensures the Project exists in the default brand's Org,
- writes the resulting Org IDs and an `ITokenProvider` (built from the PAT)
  into `ZitadelRuntimeState` (singleton). `BrandRegistry` reads the org map;
  `ZitadelClientFactory` reads the token provider to mint per-org gRPC
  management clients on demand.

**Consequences.**
- One language. No python3 / curl / bash shell-quoting fragility.
- API fails fast at startup if ZITADEL isn't reachable — the same
  property we want for any IDP-dependent service.
- mprocs goes from three procs (services / bootstrap / api) to two
  (services / api), matching the peer pro-cr-billing pattern.
- `BrandRegistryOptions.Map` collapses to `BrandRegistryOptions.BrandIds`
  (a list) — Org IDs are runtime-resolved, not config-supplied.
- `ZitadelOptions.AdminPat` becomes `ZitadelOptions.AdminPatFile`.

---

## 14. Per-brand Player records; defer cross-brand `Subject` — 2026-05-06

**Context.** With multi-brand confirmed, the question is whether a single
human registering on Brand A and Brand B should be one PAM record or two.
The legacy production system uses pattern (a) — one row regardless of
brand — which conflates a person with an account: shared wallet across
brands (illegal in many jurisdictions), one brand's compliance officer
sees other brands' data, conflicting per-jurisdiction RG limits on a
single row.

**Decision.** Each `(person × brand)` is its own `Player` row. Email
uniqueness is per-brand (composite unique index on `(brand_id, email)`).
The legacy pattern is **not** carried forward. A future optional
`Subject`/`Person` concept can link `Player`s across brands when a
specific regulatory requirement arrives (UK GamStop-style central
self-exclusion, FATF travel-rule recognition); until then, "same human
re-verifies KYC on each brand" is the accepted cost.

**Consequences.**
- Wallet, Limits, Bonus, KYC are per-Player by construction.
- Cross-brand fraud detection requires a future `Subject` layer; deferred
  until a real regulator asks.
- The legacy data migration story is "split, not preserve": each (person,
  brand) pair in the legacy `tbCustomer` becomes a separate `Player` row,
  with a stub `Subject` linking them only if mandated.

---

## 13. Brand is first-class; one ZITADEL Org per Brand — 2026-05-06

**Context.** betanything.eu is one company today, planning expansion to
LATAM and Asia with crypto. That is multi-brand (multiple consumer-facing
sites under one operator) and multi-jurisdiction. Adding a `BrandId` to
every aggregate after KYC, Wallet, Limits, Bonus all ship is much more
expensive than adding it before module #2.

**Decision.** A `Brand` concept is first-class from day one. Every
aggregate carries `BrandId`. ZITADEL Org per Brand: registration sets
`x-zitadel-orgid` on Management API calls; the `IIdentityProvider` impl
resolves Brand → Org via an injected `IBrandRegistry`. Anonymous
registration takes brand context from the `X-Brand` header (default
`betanything-eu`); authenticated requests will derive brand from the
player record itself. Email uniqueness is per-brand
(`(brand_id, email)` composite unique index).

The full `Pam.Operators` module (Brand + Jurisdiction policy registry,
admin endpoints) is deferred to before module #2. Today the registry is
a hardcoded `IBrandRegistry` reading from `appsettings:Brands:Map`,
populated by the bootstrap script's `.env.zitadel`.

**Consequences.**
- POC seeds two Orgs (`betanything-eu`, `betanything-latam-stub`) so the
  multi-brand path is exercised, not assumed.
- Ahead of jurisdictional rules: `Jurisdiction` stays a value object on
  `Player` for now; a `Pam.Operators` jurisdiction policy module
  (min age, KYC tier matrix, allowed currencies) lands when KYC does.
- ZITADEL's tenant boundary aligns with the brand boundary by design;
  no per-brand realm-import dance.

---

## 12. ZITADEL replaces Keycloak — 2026-05-06

**Context.** Decision #4 deferred a ZITADEL spike to "the operators
realm milestone." Multi-brand confirmation (decision #13) shifted that
trigger forward — committing to a Brand model is the moment to question
the IDP shape, because ZITADEL's Org/Project model maps naturally to
Brand and Keycloak's realm-per-brand pattern multiplies operational
pain (N realm imports, N admin clients, N token endpoints).

**Decision.** Replace Keycloak end-to-end with ZITADEL. Brand → Org,
single Project (`pam-player-api`) audience for the player API, machine
PAT for Management API access. The `IIdentityProvider` seam is preserved;
`KeycloakIdentityProvider` is replaced with `ZitadelIdentityProvider`.
The `player_id` claim is dropped from JWTs entirely — the IDP `sub` is
the foreign key on the Player aggregate, and PAM resolves PlayerId
server-side via that lookup. Tokens stay lean.

**Consequences.**
- One Postgres-backed self-host service replaces the JVM-heavy Keycloak
  + Keycloak-Postgres pair.
- Bootstrap is API-driven (`infra/zitadel/bootstrap.sh`) using a PAT that
  ZITADEL writes to a shared volume on first init. No realm-import +
  user-profile-schema post-import dance.
- `Keycloak.AuthServices.*` packages and `Testcontainers.Keycloak`
  removed from the central catalog (they were forward-pinned but
  unused).
- `global.json` SDK pin relaxed from `10.0.203` → `10.0.106` with
  `latestFeature` rollForward, so locally-installed SDKs >=10.0.106
  satisfy the build without per-commit `global.json` swapping.
- Supersedes decision #4.

---

## 11. Infisical removed; env-var secrets model — 2026-05-05

**Context.** A custom `InfisicalSecretsConfigurationProvider` had been
wired into `Pam.Api` along with a self-hosted Infisical stack
(`infisical`, `infisical-postgres`, `infisical-redis`) in compose.
Bootstrap (admin, org, project, machine identity) was UI-only and never
automated; dev encryption keys lived hardcoded in compose.

**Decision.** Remove Infisical end-to-end. Production secrets arrive as
env vars from whatever orchestrator runs the API; ASP.NET's default
configuration precedence (env vars > `appsettings.{env}.json` >
`appsettings.json`, with `__` mapping to `:`) handles the rest.

**Consequences.**
- One fewer service to operate locally. Frees host ports `5434`, `6381`,
  `8085`.
- No central rotation, audit, or fine-grained access control on secrets.
  Acceptable until the first real production deploy.
- A dedicated secret store (Vault / SOPS / k3s External Secrets) is now
  an open ROADMAP entry rather than a half-wired prototype.

---

## 10. xUnit v3 for new test code — 2026-05-05

**Context.** No tests existed yet; we were free to pick the version. The
xUnit 2 → 3 migration is real but the cost is bounded by the size of the
test surface. Catching it before the test surface is non-zero is much
cheaper than catching it later.

**Decision.** Standardize new test code on `xunit.v3` 3.2.2 plus
`xunit.runner.visualstudio` 3.1.5 (must always match the v3 line — never
split).

**Consequences.**
- One test project (`tests/Pam.Players.UnitTests/`) on xUnit v3 from day
  one. 21 tests, ~40 ms run.
- Future test projects copy the same csproj template; no v2-vs-v3
  compatibility headaches.

---

## 9. Persist enum-typed columns as varchar, not int — 2026-05-05

**Context.** `PlayerStatus` was originally stored as the integer ordinal.
Inserting a state mid-list reorders existing values and silently
corrupts every existing row. The same trap exists for `ActorType`.

**Decision.** Persist enums as their name via `HasConversion<string>()`
with an explicit `HasMaxLength`. Migrations that change int → varchar
must include an explicit `ALTER ... USING CASE` clause because PostgreSQL
will not auto-cast.

**Consequences.**
- Adding a new enum value is append-only at any position.
- Column reads as values that mean something to anyone running a SELECT.
- Slight storage overhead vs int — negligible.

---

## 8. Typed `Actor` for `IUserContext` and audit columns — 2026-05-05

**Context.** Audit columns previously held a single `CreatedBy: string?`
that interleaved Keycloak `sub`s (player actions), future operators-realm
`sub`s (different realm, but same shape), and the literal string
`"system"`. There was no discriminator — a regulator asking "did a Player
self-suspend or did an Operator suspend them?" required parsing.

**Decision.** Replace the string with a typed
`Actor(ActorType, Id)` — `ActorType ∈ {System, Player, Operator, Service,
Anonymous}`. Persist as a 4-column split:
`(CreatedByType, CreatedById, LastModifiedByType, LastModifiedById)`,
each `Type` column as varchar(16) via `HasConversion<string>()`.

**Consequences.**
- Audit queries can group/filter by actor type without parsing.
- Adds two columns per entity. Trivial.
- Forces every `IUserContext` implementation to map identity into the
  `Actor` shape — clean migration story when the operators realm lands.

---

## 7. Domain events dispatch post-save with bounded loop — 2026-05-05

**Context.** The `DispatchDomainEventsInterceptor` originally hooked
`SavingChangesAsync` (pre-save). Any handler with side effects could leak
state when the SaveChanges that triggered it then failed. The dispatch
loop was unbounded `while(true)` — a handler that mutates a tracked
aggregate would re-raise events forever.

**Decision.** Override `SavedChangesAsync` (post-save). Cap re-dispatch
at 8 generations and throw on exceed. Drop the sync `SavingChanges`
override entirely (forces async-only saves repo-wide).

**Consequences.**
- Handlers see committed state. No leaked side effects on rollback.
- Lost atomicity-with-DB-write for in-process handlers — acceptable
  because the only handler today logs. The outbox fills the gap when
  cross-module broker publishing arrives.
- Bounded-loop throw makes runaway-handler bugs loud instead of
  hangs.

---

## 6. Authorization fallback policy + per-IP rate-limit partitioning — 2026-05-05

**Context.** No fallback authorization policy meant any new endpoint
defaulted to anonymous unless it explicitly called `RequireAuthorization`.
The `auth-sensitive` rate-limit policy was a single fixed-window limiter,
not partitioned — five attempts a minute total fleet-wide, locking
everyone out after the first five.

**Decision.** Set `AuthorizationOptions.FallbackPolicy` to
`RequireAuthenticatedUser` (scheme `players`). Public endpoints opt out
via `.AllowAnonymous()`. Convert `auth-sensitive` to a
`PartitionedRateLimiter` keyed by `player_id` claim (when authenticated)
or forwarded client IP. Configure `ForwardedHeadersOptions` early in the
pipeline so the IP key reflects the real client behind a proxy.

**Consequences.**
- The next endpoint that forgets `RequireAuthorization` gets a 401, not a
  silent leak.
- Rate-limit windows are per-client, not per-fleet.
- Health and dev OpenAPI/Scalar endpoints must explicitly
  `.AllowAnonymous()` (already done).

---

## 5. Dependency licensing pins — 2026-05-05

**Context.** Three packages on the catalog were on the edge of going
commercial or had already gone commercial in their latest version.

**Decision.** Stay on the last free release of each:

| Package | Version | License pin |
|---|---|---|
| MediatR | 12.4.1 | last MIT (v13+ Lucky Penny commercial) |
| MassTransit (and rabbitmq / efcore) | 8.5.9 | last Apache-2.0 of v8 line (v9 Massient commercial) |
| FluentAssertions | 7.2.0 | last Apache-2.0 (v8+ Xceed commercial) |

`SecurityCodeScan.VS2019` was abandoned (last release 2022-09); removed
entirely. `Microsoft.CodeAnalysis.NetAnalyzers` covers the gap on by
default with `AnalysisMode=All`; layered SAST (CodeQL / Sonar security
rules) goes in CI when the time comes.

**Consequences.**
- Zero ongoing license cost. Security patches for v8 MassTransit run
  through end of 2026; longer-term migration is a 12–18-month decision.
- Bumping any of these majors is a deliberate license review, not a
  routine `dotnet outdated` upgrade.

---

## 4. Stay on Keycloak now; ZITADEL spike at operators-realm milestone — 2026-05-05

**Context.** Researched Keycloak vs ZITADEL vs Authentik vs ASP.NET
Identity (the approach used in the peer `pro-cr-billing` project). PAM is
regulated B2C iGaming with self-exclusion, MFA-per-realm, federation, and
auditor-checkable password policies — all of which favor an external
IDP over in-app Identity.

**Decision.** Stay on Keycloak for the player-only phase. Schedule a
2-day ZITADEL spike for the moment we're about to add the operators
realm — ZITADEL's Org/Project model is a better fit for the planned
`Partner` aggregate than two Keycloak realms. ASP.NET Identity ruled out
for the player auth path.

**Consequences.**
- The `IIdentityProvider` interface stays the swap seam; we keep
  Keycloak-specific concepts (realm name, custom-attribute API shape) on
  the Keycloak side of it.
- We accept Keycloak's operational weight (JVM, painful upgrades,
  realm-import friction) for the duration of the player-only phase.
- ROADMAP entry "ZITADEL spike (alternative to Keycloak)" tracks this.

---

## 3. Patterns to lift from `pro-cr-billing` — 2026-05-05

**Context.** Reviewed the peer billing project's auth and infrastructure
code. The auth approach (ASP.NET Identity + custom JWT + RefreshToken
rotation) is wrong for PAM but several adjacent patterns are well-shaped
and reusable.

**Decision.** Adopt these regardless of IDP choice:

- `ApiKeyAuthenticationHandler` skeleton — for the future `Pam.GameWallet`
  host (HMAC-signed game-provider callbacks) and webhooks. **Pending.**
- `ForwardedHeadersOptions` configured with `KnownIPNetworks.Clear()` +
  `KnownProxies.Clear()`. **Done** (commits `41f3a50`, `7253a51`).
- Rate-limit `OnRejected` with structured logging and a `Retry-After`
  header. **Done** (`41f3a50`).
- API versioning via `Asp.Versioning.Http` (URL/query/header readers)
  before more endpoints land. **Pending** (package pinned, not wired).
- `existence` queries on the registration flow (`/exists/email`,
  `/exists/identification`) — better UX than slow-fail-on-submit.
  **Pending.**
- Hierarchical role-policy pattern (`Accountant` policy accepts `Owner`
  too) for `operator.support` ⊂ `operator.compliance` ⊂ `operator.admin`
  when the operators realm lands. **Pending.**

**Not** lifted: `User : IdentityUser<Guid>`, HS256 with a symmetric key,
hardcoded password policy in `AddIdentityCore`, `Database.Migrate()` on
startup. Those work for billing's B2B model but regress PAM's regulated
B2C posture.

**Consequences.** PAM's auth direction stays IDP-driven; we get
production-quality patterns for the surrounding pipeline without coupling
the regulated parts of the system to billing's identity model.

---

## 2. Modular monolith with explicit module seams (recap from existing docs) — pre-session

**Context, Decision, Consequences.** See `ARCHITECTURE.md` sections
*Modular monolith, not microservices (yet)* and *Plural module
assemblies* — those decisions predate this log and remain authoritative.

---

## 1. Keycloak owns credentials; PAM owns the canonical Player (recap from existing docs) — pre-session

**Context, Decision, Consequences.** See `ARCHITECTURE.md` section
*Keycloak owns credentials; PAM owns the canonical player* — predates
this log and remains authoritative.
