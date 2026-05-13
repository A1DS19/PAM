# Testing

What each suite covers, why we drew the lines, and how to run them.

## Suite inventory

| Suite | Location | Tests | Runtime | Covers |
|---|---|---|---|---|
| Operators unit | `api/tests/Pam.Operators.UnitTests/` | 9 | ~100ms | `Brand` aggregate + `CreateBrand` validator |
| Identity unit | `api/tests/Pam.Identity.UnitTests/` | 35 | ~1s | Validators + `PermissionResolver` over EF in-memory |
| Ingest unit | `api/tests/Pam.Ingest.UnitTests/` | 2 | `<100ms` | `VendorTransaction` aggregate factory placeholders |
| Players unit | `api/tests/Pam.Players.UnitTests/` | 1 | `<100ms` | `Player.Create` placeholder |
| Wallet unit | `api/tests/Pam.Wallet.UnitTests/` | 1 | `<100ms` | `Account.Open` placeholder |
| Shared unit | `api/tests/Pam.Shared.UnitTests/` | 25 | ~200ms | Caching behavior + cache-key generator, `AuditBehavior`, `OpenTelemetryBehavior`, `SensitiveJsonRedactor` |
| Architecture | `api/tests/Pam.ArchitectureTests/` | 9 | ~250ms | NetArchTest rules: module boundary, event shape, aggregate inheritance |
| Integration | `api/tests/Pam.IntegrationTests/` | 10 | ~3s (+10–20s first-run container boot) | Health probes, auth fence, outbox reconciliation, Testcontainers (SQL / RabbitMQ / Redis) |

**Total: 92 tests.** Unit + architecture finish under 2s; integration
is dominated by one-time container boot.

## Unit tests (one project per module)

**In scope** — aggregate invariants + validator behavior. No DB, no
broker, no DI graph.

- Aggregate factory methods + state transitions (`Brand.Create` stamps
  slug, sets status Active, raises `BrandCreatedDomainEvent`).
- Every validator behind a real endpoint (`CreateBrand`, `CreateUser`,
  `ChangePassword`, `MfaVerify`, `ForgotPassword`, `ResetPassword`,
  `ConfirmEmail`, `LoginRecoveryCode`, `MfaDisable`, `ListUsers`).
- `PermissionResolver` over EF in-memory — the **only** place we use
  in-memory EF, because resolving a join table benefits from a real
  querying provider even if not SQL Server.
- **Shared** suite covers cross-cutting infra: `CachingBehavior` +
  `CacheKeyGenerator`, `AuditBehavior`, `OpenTelemetryBehavior`,
  `SensitiveJsonRedactor`. Lives in `Pam.Shared.UnitTests` rather than
  any one module since it's reused everywhere.

**Out of scope** — anything that needs `SaveChangesAsync` to commit to
a real DB, or that needs the bus to accept a publish. Those graduate
to integration. `Players`, `Wallet`, and `Ingest` aggregate behavior
beyond placeholders lands with the first real features.

**Conventions**

- xUnit v3 only (`xunit.v3` + `xunit.runner.visualstudio` 3.x — never
  mix v2 and v3).
- FluentAssertions **pinned at 7.2.0** — last Apache-2.0 release.
  v8+ is Xceed commercial. Do not bump.
- No mocking `DbContext` or `IPublishEndpoint`. Either the test doesn't
  need them, or it should graduate to integration.

## Architecture tests (`Pam.ArchitectureTests`)

Encode `ARCHITECTURE.md` structural rules as automated checks. Failed
arch tests block the build.

| Test | Rule | Why |
|---|---|---|
| Cross-module internals tests (Operators↔Identity↔Notifications) | `Pam.<X>` may not directly reference `Pam.<Y>` | `*.Contracts` is the only seam |
| `Domain_Events_Implement_IDomainEvent` | Types ending in `DomainEvent` implement `IDomainEvent` | Interceptor finds by interface; naming makes a single grep find every event |
| `Integration_Events_End_In_IntegrationEvent` | Types in `*.IntegrationEvents.*` end in `IntegrationEvent` | Naming = lookup convention |
| `Aggregates_Inherit_From_Aggregate_Or_Entity` | Concrete `IAggregate` implementers derive from `Aggregate<>` | The base class carries the `DomainEvents` list the dispatcher reads |

**Note on `TestResult`** — `NetArchTest.Rules.TestResult` collides with
`Xunit.TestResult`. All files alias the NetArchTest one:
`using ArchTestResult = NetArchTest.Rules.TestResult;`.

**Out of scope (deliberate)** — Players/Wallet rules until those
modules grow real cross-module touchpoints. Naming rules for handlers
/ endpoints / configurations — enforced by review, locking patterns
prematurely is noise.

## Integration tests (`Pam.IntegrationTests`)

Prove the host boots and serves end-to-end against real infrastructure.
Catches what unit tests can't: migrations applying cleanly, DI graph
composing, real broker round-trips, real HTTP pipeline.

**Harness**

- `PamContainersFixture : IAsyncLifetime` boots SQL Server 2022 +
  RabbitMQ 4 + Redis 7 via Testcontainers. Shared as an
  `ICollectionFixture` — one boot per session.
- `PamApiFactory : WebApplicationFactory<Program>` overrides
  `ConnectionStrings` and `MessageBroker` via
  `ConfigureAppConfiguration`. Service-collection overrides are too
  late — `Program.cs` `await`s `ConnectionMultiplexer.ConnectAsync` at
  the top level **before** `builder.Build()`.
- RabbitMQ credentials come from
  `containers.Rabbit.GetConnectionString()` (fresh non-`guest`
  credentials per container).
- Unique `DataProtection:ApplicationName` per `PamApiFactory` instance
  to isolate DP keys across parallel runs.

**Current coverage** (10 tests across health, auth fence, and outbox
reconciliation):

| Area | What's covered |
|---|---|
| Health probes | `LiveProbe_Returns_Healthy` (`GET /health/live`), `ReadyProbe_Returns_Healthy_When_Dependencies_Up` (`GET /health/ready` — resolves `HealthCheckService` directly so failures name **which** dependency broke) |
| Auth fence | Anonymous `POST /v1/operators/brands` → 401 |
| Outbox reconciler | Orphan business rows get republished + idempotency (`OutboxReconciliationTests` — see `INGEST.md`) |

The point of the gate is the **harness**, not coverage. Adding tests
for any endpoint is mechanical from here. Auth'd end-to-end coverage
(create → get round-trip) needs a test-only bearer-token seam — next
integration-test PR.

**Conventions**

- `TestContext.Current.CancellationToken` for async calls (xUnit1051).
- `HttpClient.GetAsync` takes a `Uri`, not a string (CA2234).
- New `PamApiFactory` per test method (`await using`) — sharing races
  DP keyring writes and hosted-service shutdown.

**Out of scope (deliberate)** — perf/load (needs realistic data),
migration rollback (manual against a real backup), multi-replica
integration (useful follow-up, not blocking).

## Coverage gaps (intentional)

- **Auth-aware integration tests** — needs a test-only bearer seam.
  Biggest current gap.
- **Outbox transactional semantics** — publish + force-fail-save and
  assert no `outbox_message` row written. Lands when the first
  cross-module consumer ships.
- **Multi-replica** — cookies from replica A validating on replica B
  (the point of the DP keys PR).
- **Rate limiter under concurrency** — Redis-backed limiter across
  replicas.
- **Players + Wallet beyond `.Create`** — with real features.
- **Mutation tests** — out of scope until coverage is broader.

## Running

```bash
make -C api test                       # all suites
dotnet test api/pam.slnx --nologo      # equivalent

# A specific suite
dotnet test api/tests/Pam.Operators.UnitTests/Pam.Operators.UnitTests.csproj --nologo
dotnet test api/tests/Pam.ArchitectureTests/Pam.ArchitectureTests.csproj --nologo
dotnet test api/tests/Pam.IntegrationTests/Pam.IntegrationTests.csproj --nologo

# Filter
dotnet test api/pam.slnx --nologo --filter "FullyQualifiedName!~Pam.IntegrationTests"
dotnet test api/pam.slnx --nologo --filter "FullyQualifiedName~CreateBrand_Requires_Authentication"
```

Integration suite requires Docker. Testcontainers manages its own
SQL/Rabbit/Redis containers — separate from the long-lived ones in
`docker-compose.yml`.

### When integration tests flake

Most common: "container didn't come up in time" — symptoms are
timeouts, `Broker unreachable`, `connection refused`.

1. `docker ps` — daemon actually running?
2. Disk space — full disks surface as opaque container-start failures.
3. Resource limits — Rabbit can take 15s+ to be reachable on a loaded
   laptop.

If `ReadyProbe_Returns_Healthy_When_Dependencies_Up` fails, the
exception message names which health check returned non-Healthy and
why — the test resolves `HealthCheckService` directly.

## Conventions cheat-sheet

| What | Rule |
|---|---|
| Test framework | xUnit v3 only |
| Assertions | FluentAssertions 7.2.0 (Apache-2.0 last) — never 8.x |
| Mocking | NSubstitute when needed; never for `DbContext`/`IPublishEndpoint` |
| Containers | Testcontainers 4.x — `new MsSqlBuilder("…")` ctor (parameterless+`.WithImage` is obsolete). Runs under Rosetta on Apple Silicon |
| Async cancellation | `TestContext.Current.CancellationToken` (xUnit1051) |
| `HttpClient.GetAsync` | `Uri`, not string (CA2234) |
| Integration factory lifecycle | one per test method, `await using` |
| `Program` access from tests | `public partial class Program;` trailer in `Program.cs` |
