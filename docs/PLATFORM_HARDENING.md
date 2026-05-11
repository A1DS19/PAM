# Platform hardening — 2026-05-11

This document captures the platform-hardening pass: what we did, why,
and what's now possible that wasn't before. It complements
[ARCHITECTURE.md](ARCHITECTURE.md) (the *why* behind the design) and
[ROADMAP.md](ROADMAP.md) (the trigger-gated punch list).

## The prompt

After Phase 3 (`Pam.Identity`) shipped, a technical assessment scored
the system **38 / 100** against the question "can this handle millions
of records / requests today?" The score was low because:

- Two of the three modules that actually hold the data — `Pam.Players`,
  `Pam.Wallet` — didn't exist.
- The system had never proven it could run with more than one replica:
  in-memory rate limiter, ephemeral Data Protection keys, ephemeral
  OpenIddict signing material, race-prone startup seeder.
- No outbox: integration events could leak or vanish under crash.
- No integration tests, no architecture tests, no end-to-end harness.
- No telemetry leaving the process.

The work below took that 38 to ~62–65 by closing the *pre-replica*
blockers, establishing the test gate, and scaffolding the two
remaining modules that will host the bulk of the data.

## What landed (seven PRs, in dependency order)

### 1. OTLP pipeline + Grafana LGTM — `chore/otel-pipeline`

Traces, metrics, and logs leave Pam.Api over OTLP. Local dev runs the
`grafana/otel-lgtm` all-in-one container (Tempo + Mimir + Loki + Grafana
+ Collector — Grafana UI on `:3001`, OTLP gRPC on `:4317`, HTTP on
`:4318`). Production swaps the endpoint via
`OTEL_EXPORTER_OTLP_ENDPOINT` without code changes.

Resource attributes (`service.name`, `service.version`,
`deployment.environment`, `host.name`) tag every signal so dashboards
can segment by env/instance once more than one runs. Instrumentation:
AspNetCore, HttpClient, EF Core, Npgsql, MassTransit, `Pam.*`
ActivitySources/Meters. Logs flow through Serilog's OpenTelemetry sink
alongside the existing Console/Seq sinks.

### 2. Shared signing material + DP keys — `chore/multi-replica-keys`

Two pieces of state that have to be shared before a second replica can
serve traffic alongside the first:

- **Data Protection master keyring** persists to
  `identity.data_protection_keys` via `IDataProtectionKeyContext` on
  `IdentityDbContext`. Cookies + OpenIddict state strings issued by
  one replica now validate on another. `SetApplicationName(...)`
  isolates keys across environments sharing a database
  (`pam-api/Development` ≠ `pam-api/Production`) — keys from staging
  won't decrypt prod cookies, which is the desired behavior.

- **OpenIddict signing + encryption certificates** load from PFX paths
  configured under `OpenIddict:{Signing,Encryption}Certificate:{Path,Password}`
  in non-Development. Mount the same PFX into every replica; rotate by
  swapping the file and rolling restarts. Development keeps the
  persisted self-signed certs from `AddDevelopment*Certificate`.

**Why Postgres for DP keys, not Redis.** Security material shouldn't be
tied to cache availability — losing Redis (cache) shouldn't invalidate
every cookie. Identity already has Postgres, so no new dependency.

**Why PFX files, not env-var-base64-PFX.** Maps cleanly to k8s
Secret-mounted-as-file, systemd `LoadCredential=`, and Swarm secrets.

**Fail-fast** outside Development if either cert path is unset —
silently falling back to ephemeral keys would break auth subtly.

### 3. Redis-backed rate limiter — `chore/redis-rate-limiter`

The in-memory `FixedWindow`/`SlidingWindow` limiters partition state
per-process. Two replicas behind a load balancer each track their own
counts, so an attacker hitting `/login` at 2× the configured rate gets
served by alternating instances. Both policies now use the
`RedisRateLimiting` package's distributed variants
(`RedisFixedWindowRateLimiter` for `auth-sensitive`,
`RedisSlidingWindowRateLimiter` for `api-default`).

A single `IConnectionMultiplexer` registered as a DI singleton backs
the limiter, the new Redis health check, and any future module needing
a shared cache. `AbortOnConnectFail=false` avoids blocking boot on a
briefly-unreachable Redis; rate-limited endpoints surface 5xx while
Redis is down — intentional fail-closed posture for auth.

`SegmentsPerWindow` drops from the sliding-window policy: the Redis
impl uses a sorted-set sliding window keyed by timestamp (textbook),
not segmented buckets. More accurate, not a regression.

### 4. EF Core outbox + pre-save dispatch — `chore/operators-outbox`

The publish-then-commit-fail race window: a crash between
`IPublishEndpoint.Publish` and the broker's ack drops the event; a
failed `SaveChanges` after a successful publish leaves a phantom event
referring to a row that never existed. Both produce states consumers
and audit can't recover.

**Wiring.** `MassTransit.EntityFrameworkCore` on Pam.Operators;
`OperatorsDbContext` adds inbox/outbox model entities.
`OperatorsModule.ConfigureOutbox` composes the bus-side outbox setup
(`UsePostgres`, `UseBusOutbox`, `QueryDelay`, `DuplicateDetectionWindow`);
`Program.cs` passes it through `AddPamMassTransit`'s new `configureBus`
parameter. Future publishing modules add their delegate the same way.

**Pre-save dispatch.** `DispatchDomainEventsInterceptor` moves from
`SavedChangesAsync` to `SavingChangesAsync`. This is the precondition
for the outbox — handlers' `IPublishEndpoint.Publish` calls now enrol
in the same transaction as the `SaveChanges` that triggered them, so
`outbox_message` rows commit atomically with the aggregate write. A
background `EntityFrameworkOutboxDeliveryService` polls the table and
forwards to RabbitMQ.

**Trade-offs locked in.**
- Domain handlers see pre-commit state. Queries against freshly-written
  rows return nothing — pass data through the event payload instead.
- A throwing handler rolls back the whole `SaveChanges`. Atomic by
  design. Don't put best-effort side effects (logging, metrics) in
  domain handlers; put them in integration-event consumers.

### 5. Concurrent-boot seeder lock — `chore/seeder-advisory-lock`

The seeder is idempotent per-step but the *steps* race when two
replicas boot concurrently: replica A and replica B both notice the
Owner role missing, both call `roleManager.CreateAsync`, the second
fails with a UNIQUE violation that surfaces as a 500 during startup.
EF migrations already serialise through `__EFMigrationsHistory`, but
seeding had no such lock.

`IdentitySeeder.SeedAsync` is now wrapped in a transaction +
`pg_advisory_xact_lock(100_001)`. The `xact` variant releases on
commit/rollback so a crash mid-seed doesn't orphan the lock. Once
replica A finishes, replica B acquires, finds every step's
precondition met (idempotent), commits quickly.

Reserve **100_000–100_999** for module bootstrap seeders so future
modules don't have to think about lock-key collisions per-module.

### 6. Test gate — `chore/test-gate`

Three pieces ROADMAP gated Wallet on.

**Integration tests** (`tests/Pam.IntegrationTests/`)
- `PamContainersFixture` boots Postgres 17 + RabbitMQ 4 + Redis 7 via
  Testcontainers, shared as an `ICollectionFixture` so the ~10–20s
  boot cost is paid once per test session.
- `PamApiFactory : WebApplicationFactory<Program>` overrides
  `ConnectionStrings` + `MessageBroker` via
  `ConfigureAppConfiguration` — services-collection overrides are
  too late because `Program.cs` reads config **before**
  `builder.Build()` (the Redis multiplexer is awaited at the top
  level). RabbitMQ creds come from the container's connection
  string, not `guest/guest`.
- Initial coverage: live + ready health probes, anonymous
  create-brand → 401 (auth fence intact). The point of this PR is
  the harness, not full coverage — coverage grows when endpoints land.

**Architecture tests** (`tests/Pam.ArchitectureTests/`) — 8 NetArchTest
rules:
- Module boundary: `Pam.<X>` may not reference `Pam.<Y>` directly. Only
  `Pam.<Y>.Contracts` is allowed.
- Domain events ending in `DomainEvent` must implement `IDomainEvent`.
- Types in `*.IntegrationEvents` namespaces must end in
  `IntegrationEvent`.
- Aggregates implementing `IAggregate` must derive from `Aggregate<>`.

**CorrelationId middleware** (`src/Shared/Pam.Shared/Http/`). Reads
`X-Correlation-Id`, generates `Guid.CreateVersion7().ToString("N")` if
absent, echoes on response, sets the id on `Activity.Current` as both
baggage and a tag, `BeginScope`'s a `CorrelationId` log property.

Registered before `UseSerilogRequestLogging` so the request-log entry
itself carries the id. MassTransit's diagnostic instrumentation
propagates Activity baggage through publishes, so the outbox carries
the id to async consumers — exactly the gap OTel's `traceparent`
doesn't cover.

### 7. Module #3 + #4 scaffolds — `feat/players-scaffold` + `feat/wallet-scaffold`

Both follow the established per-module pattern (`Pam.<X>` /
`Pam.<X>.Contracts` / `tests/Pam.<X>.UnitTests`; module class with
`Add*Module` / `Use*ModuleAsync`; schema-per-module + snake_case;
design-time factory; one placeholder test).

**Players** — schema `players`. `Player(BrandId, Email)` placeholder.
Composite UNIQUE `(brand_id, email)` because email uniqueness is
per-brand, not global — different brands can legitimately share player
emails. `PlayersDbContext.OnModelCreating` leaves a placeholder
comment for the brand-scoped global query filter; wiring requires
`IBrandContext` reading from the JWT `brand_id` claim, which lands
with the first authenticated player endpoint.

**Wallet** — schema `wallet`. `Account(BrandId, PlayerId, Currency)`
placeholder. Composite UNIQUE `(brand_id, player_id, currency)` —
one account per (player, currency) per brand. ISO 4217 currency,
fixed-length 3. Real double-entry ledger (`LedgerEntry` /
`Transaction` with sum-to-zero invariants, balance snapshots,
multi-currency conversion) lands in follow-up PRs.

**Wallet outbox is wired on day one.** `WalletDbContext.OnModelCreating`
calls `AddInboxStateEntity / AddOutboxMessageEntity / AddOutboxStateEntity`;
the initial migration provisions the tables;
`WalletModule.ConfigureOutbox` is composed with
`OperatorsModule.ConfigureOutbox` in `Program.cs`'s call to
`AddPamMassTransit`. Ledger writes will commit atomically with their
`LedgerEntryPosted` integration event from the very first feature PR
— non-negotiable per ARCHITECTURE.md.

## Where this leaves the assessment

| Capability | Before | After |
|---|---|---|
| Run N>1 replicas behind a load balancer | No — cookies/tokens/rate-limit state per-instance | Yes — shared via Postgres + Redis |
| Integration events survive a crash | No — publish-then-commit-fail loses messages | Yes — outbox commits atomically with the row |
| Concurrent-replica boot | Race on UNIQUE constraints in seed | Serialised on `pg_advisory_xact_lock` |
| End-to-end debugging across async flows | Partial — OTel covers HTTP↔HTTP only | CorrelationId fills the async gap |
| Telemetry leaves the process | No | OTLP traces + metrics + logs |
| Module-boundary rules enforced | Code review only | NetArchTest gate |
| Real-DB integration tests | None | Testcontainers harness + 3 representative tests |
| Modules holding the data | Operators + Identity + Notifications (skeleton) | + Players + Wallet (both scaffolded, Wallet outbox-ready) |

Assessment score moves from **38 → ~62–65**. The remaining gap to
break 70 is real Wallet domain code (double-entry ledger +
partitioning), a real Players model (KYC, sessions, limits), and the
read-side / cache / CDC story that turns "tables exist" into
"millions of requests routed off the write DB."

## What's still gating higher scale

In rough order of impact, the next things to attack:

1. **Real Wallet domain code.** `LedgerEntry` / `Transaction` with
   sum-to-zero invariants, balance snapshots, multi-currency
   conversion. This is the module that will actually hold the
   "millions of records." Partitioning strategy (time-based on
   `outbox_message` and `ledger_entry`), hot-path indexes, and
   append-only patterns get designed during this work.

2. **Brand-scoped global query filter.** Placeholders are in both
   `PlayersDbContext` and `WalletDbContext`. Needs an `IBrandContext`
   reading the JWT `brand_id` claim, mirrored from
   `HttpUserContext`. Lands with the first authenticated player /
   wallet endpoint.

3. **Read-side scaling.** Pagination is offset-based today (`page`,
   `pageSize`). Cursor pagination on the high-volume reads (Players
   list, Wallet ledger) will be needed before record counts climb. No
   read replicas, no projection store, no L2 cache wired yet — Redis is
   provisioned but only used for rate limiting.

4. **Auth-aware integration tests.** The current harness verifies the
   401 fence. A test-only auth seam (issue a dev bearer token) lets
   create-brand → get-brand round trip run authenticated.

5. **Audit module.** Regulatory audit (immutable, hash-chained) is
   still deferred. First jurisdiction with a hard requirement triggers
   it.

6. **Production secret store + CI/CD.** Env-var-driven config works
   for now; HashiCorp Vault / SOPS / k3s External Secrets gets picked
   when a real production deploy target lands. No CI pipeline exists
   yet — `make test` is the only gate.

See [ROADMAP.md](ROADMAP.md) for the trigger-gated punch list and the
SHIPPED entries for each of the items above.
