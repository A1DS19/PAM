# Testing

This document is the topical reference for the test suites: what each
suite covers, why we drew the lines where we did, what's intentionally
**not** tested yet, and how to run them.

For the narrative behind the test gate landing, see
[PLATFORM_HARDENING.md](PLATFORM_HARDENING.md); for the trigger-gated
"what's next" view, see the *Tests* section of
[ROADMAP.md](ROADMAP.md).

## Suite inventory

| Suite | Location | Tests | Runtime | What it covers |
|---|---|---|---|---|
| Operators unit | `tests/Pam.Operators.UnitTests/` | 18 | ~100ms | `Brand` aggregate behavior + `CreateBrand` validator |
| Identity unit | `tests/Pam.Identity.UnitTests/` | 52 | ~1s | 9 FluentValidation validators + `PermissionResolver` against EF Core in-memory |
| Players unit | `tests/Pam.Players.UnitTests/` | 1 | <100ms | `Player.Create` placeholder (scaffold) |
| Wallet unit | `tests/Pam.Wallet.UnitTests/` | 1 | <100ms | `Account.Open` placeholder (scaffold) |
| Architecture | `tests/Pam.ArchitectureTests/` | 8 | ~250ms | NetArchTest rules: module boundary, domain/integration event shape, aggregate inheritance |
| Integration | `tests/Pam.IntegrationTests/` | 3 | ~3s (incl. ~10–20s first-run container boot) | Health probes + auth fence over real Postgres/RabbitMQ/Redis via Testcontainers |

**Total: 83 tests.** Architecture + unit suites finish in well under
2 seconds; integration is dominated by the one-time container boot.

## Per-suite rationale

### Unit tests (one project per module)

**Purpose**: lock down aggregate-level invariants and validator
behavior. No DB, no broker, no DI graph — fast, deterministic,
runnable on any machine without docker.

**What's in scope**:
- Aggregate factory methods + state transitions (e.g. `Brand.Create`
  stamps the slug, sets status to Active, raises
  `BrandCreatedDomainEvent`).
- FluentValidation rules — every input contract that hits a real
  endpoint has its validator covered (`CreateBrand`, `CreateUser`,
  `ChangePassword`, `MfaVerify`, `ForgotPassword`, `ResetPassword`,
  `ConfirmEmail`, `LoginRecoveryCode`, `MfaDisable`, `ListUsers`).
- `PermissionResolver` against the EF Core in-memory provider — the
  only place we use in-memory EF, because the resolver's job is to
  walk a join table, and that's relational shape that benefits from a
  real querying provider even if it isn't Postgres.

**Out of scope**:
- Anything that requires `DbContext.SaveChangesAsync` to commit to a
  real database. Those live in integration tests.
- Anything that requires the bus to actually accept a publish. Same.
- `Players` and `Wallet` aggregate behavior beyond `.Create` — those
  modules are scaffold-only; behavior tests land alongside the first
  features.

**Conventions**:
- xUnit v3 (`xunit.v3` package, NOT `xunit` v2 — the v3 runner is in
  `xunit.runner.visualstudio` 3.x; never mix v2 and v3).
- FluentAssertions **pinned at 7.2.0** — last Apache-2.0 release. v8+
  moved to the Xceed commercial license. Do not bump without legal
  sign-off.
- No NSubstitute mocking of `DbContext` or `IPublishEndpoint`. Either
  the test doesn't need them (pure aggregate behavior) or it should
  graduate to integration.

### Architecture tests (`Pam.ArchitectureTests`)

**Purpose**: encode the structural rules from
[ARCHITECTURE.md](ARCHITECTURE.md) as automated checks so they survive
refactoring pressure. Failed arch tests block the build the same way
failed unit tests do.

**What each rule encodes**:

| Test | Rule | Why it matters |
|---|---|---|
| `Operators_Does_Not_Depend_On_Identity_Internals` | `Pam.Operators` may not reference `Pam.Identity` directly | Cross-module coupling makes future microservice extraction expensive; `*.Contracts` is the only allowed seam |
| `Operators_Does_Not_Depend_On_Notifications_Internals` | same, `Pam.Operators` → `Pam.Notifications` | same |
| `Identity_Does_Not_Depend_On_Operators_Internals` | same, reverse direction | same |
| `Notifications_Does_Not_Depend_On_Identity_Internals` | same | same |
| `Notifications_Does_Not_Depend_On_Operators_Internals` | same | same |
| `Domain_Events_Implement_IDomainEvent` | types ending in `DomainEvent` implement `IDomainEvent` | The interceptor finds events by interface, not by name — but the naming convention is what makes a single grep find every event |
| `Integration_Events_End_In_IntegrationEvent` | types in `*.IntegrationEvents.*` end in `IntegrationEvent` | Same reasoning — naming is the lookup convention, this enforces it |
| `Aggregates_Inherit_From_Aggregate_Or_Entity` | concrete `IAggregate` implementers derive from `Aggregate<>` | The `Aggregate<T>` base class is what carries the `DomainEvents` list the dispatcher reads from |

**Out of scope for the gate (deliberately)**:
- Players and Wallet aren't yet exercised by the boundary rules. Add
  them when those modules grow real cross-module touchpoints — adding
  rules over empty modules is noise.
- Naming rules for handlers / endpoints / configurations. Convention
  is enforced by code review; arch tests would lock the patterns
  prematurely.

**Note on `TestResult`**: `NetArchTest.Rules.TestResult` and
`Xunit.TestResult` collide. Both files alias the NetArchTest one:
`using ArchTestResult = NetArchTest.Rules.TestResult;`.

### Integration tests (`Pam.IntegrationTests`)

**Purpose**: prove the host actually boots and serves requests
end-to-end against real infrastructure. Catches everything unit tests
can't: EF migrations applying cleanly, the DI graph composing, real
broker round-trips, real Identity flow, real HTTP pipeline ordering.

**The harness**:

- `PamContainersFixture : IAsyncLifetime` boots Postgres 17 + RabbitMQ
  4 + Redis 7 via Testcontainers. Shared as an `ICollectionFixture`
  via `PamContainersCollection`, so the ~10–20s boot cost is paid
  once per test session, not per test class.
- `PamApiFactory : WebApplicationFactory<Program>` overrides
  `ConnectionStrings` and `MessageBroker` via
  `ConfigureAppConfiguration`. Services-collection overrides are too
  late — `Program.cs` awaits `ConnectionMultiplexer.ConnectAsync` at
  the top level **before** `builder.Build()`, so config has to be in
  place before the host is built.
- RabbitMQ credentials come from
  `containers.Rabbit.GetConnectionString()`, parsed for user/pass.
  Testcontainers rolls fresh non-`guest` credentials per container.
- A unique `DataProtection:ApplicationName` per `PamApiFactory`
  instance keeps DP keys isolated across parallel test runs.

**Current coverage**:

| Test | Verifies |
|---|---|
| `LiveProbe_Returns_Healthy` | `GET /health/live` returns 200 — host is up, route is mapped |
| `ReadyProbe_Returns_Healthy_When_Dependencies_Up` | `GET /health/ready` returns 200 — Postgres + Redis + Rabbit all reachable. Uses `HealthCheckService` directly so failures report **which** dependency broke, not just "Unhealthy" |
| `CreateBrand_Requires_Authentication` | Anonymous `POST /v1/operators/brands` → 401. The fallback authorization policy is intact |

**Why these three, and not more**:
- The point of the test gate is the *harness*, not coverage. Once
  the harness is in place, adding a test for any endpoint is mechanical.
- Authenticated end-to-end coverage (e.g. create-brand → get-brand
  round trip) needs a test-only auth seam to issue a dev bearer
  token. That seam isn't built yet — it's the next integration-test
  PR.

**Conventions inside integration tests**:
- xUnit v3 quirks: `TestContext.Current.CancellationToken` for any
  async call that accepts one (analyzer xUnit1051 enforces this).
- `HttpClient.GetAsync` takes a `Uri`, not a string (CA2234).
- New `PamApiFactory` per test method (`await using`) — sharing
  factories across tests would race the DP keyring writes and
  hosted-service shutdown.

**Out of scope for the gate (deliberately)**:
- Performance / load tests. The infra is wired but a "1k req/s"
  baseline test needs realistic data and isn't useful before Wallet
  has real domain code.
- Migration rollback tests. EF migrations are forward-only; rollback
  is a manual process that runs against a real Postgres backup.
- Multi-replica integration test (boot two `PamApiFactory` instances
  against the same containers, verify cookie/token cross-replica
  validation). This is the natural follow-up to the pre-replica-safety
  PRs — useful, but not blocking.

### What none of these suites cover yet

Listed here so the absence is intentional, not accidental:

- **Auth-aware integration tests.** As above — needs a test-only
  bearer seam. Most of the actual endpoint logic lives behind auth,
  so this is the biggest coverage gap.
- **Outbox transactional semantics.** The unit tests cover validators
  and aggregate behavior; the integration tests stop at health
  probes. A test that publishes a `BrandCreatedIntegrationEvent`,
  forces a `SaveChanges` failure, and asserts no `outbox_message`
  row was written would be the canonical assertion for the
  pre-save-dispatch + outbox combo. Lands when the first cross-module
  consumer ships in Pam.Notifications.
- **Multi-replica behavior.** Cookies issued by replica A validate on
  replica B (the whole point of PR #7's DP keys work). No test
  exercises this yet.
- **Rate limiter under concurrency.** The Redis-backed limiter
  enforces global counts; nothing tests the "6 requests across 2
  replicas in 1 minute → 6th gets 429" case yet.
- **Players + Wallet beyond `.Create`.** Behavior tests land with the
  real features.
- **Mutation tests.** Out of scope until coverage is broader.

## Running the tests

### All suites

```bash
make test
# or, equivalently:
dotnet test pam.slnx --nologo
```

The integration suite requires Docker (or Podman with a Docker
socket) running. Testcontainers will fail with a clear "Docker
endpoint unreachable" error if it isn't — no other dev
infrastructure is required (Testcontainers manages its own
Postgres/Rabbit/Redis containers, separate from the long-lived ones
in `docker-compose.yml`).

### A specific suite

```bash
# unit only — Operators
dotnet test tests/Pam.Operators.UnitTests/Pam.Operators.UnitTests.csproj --nologo

# architecture rules
dotnet test tests/Pam.ArchitectureTests/Pam.ArchitectureTests.csproj --nologo

# integration only (needs Docker)
dotnet test tests/Pam.IntegrationTests/Pam.IntegrationTests.csproj --nologo
```

### Filtering

```bash
# everything except integration (offline-friendly)
dotnet test pam.slnx --nologo --filter "FullyQualifiedName!~Pam.IntegrationTests"

# a single test method
dotnet test pam.slnx --nologo --filter "FullyQualifiedName~CreateBrand_Requires_Authentication"
```

### When integration tests flake

The most common failure mode is "container didn't come up in time".
Symptoms: timeouts, `Broker unreachable`, `connection refused`. Order
of things to check:

1. `docker ps` — is Docker actually running? Testcontainers needs a
   live daemon.
2. Disk space — Docker creates fresh containers per test run; a full
   disk surfaces as opaque container-start failures.
3. Resource limits — boot times stretch under memory pressure. The
   Rabbit container especially can take 15s+ to be reachable on a
   loaded laptop.

If `ReadyProbe_Returns_Healthy_When_Dependencies_Up` fails, the
exception message names which health check returned non-Healthy and
why — the test resolves `HealthCheckService` directly rather than
relying on the default response writer's bare `"Unhealthy"` body, so
you don't have to dig.

## Conventions cheat-sheet

| What | Rule | Lives where |
|---|---|---|
| Test framework | xUnit v3 only | `Directory.Packages.props` pins `xunit.v3` 3.x and `xunit.runner.visualstudio` 3.x |
| Assertions | FluentAssertions 7.2.0 (Apache-2.0 last) | Don't bump to 8.x — license change |
| Mocking | NSubstitute when needed; not for `DbContext`/`IPublishEndpoint` | — |
| Containers | Testcontainers 4.x | New API uses image-in-constructor (`new PostgreSqlBuilder("postgres:17")`) — the parameterless ctor + `.WithImage(...)` is obsolete |
| Cancellation in async tests | `TestContext.Current.CancellationToken` | xUnit1051 analyzer enforces |
| `HttpClient.GetAsync` | Pass `Uri`, not `string` | CA2234 analyzer enforces |
| One factory per integration test | `await using var factory = new PamApiFactory(containers);` | Sharing trips DP keyring races |
| `Program` access from tests | `public partial class Program;` trailer in `Program.cs` | Required for `WebApplicationFactory<Program>` to bind to the top-level-statement-synthesized type |
