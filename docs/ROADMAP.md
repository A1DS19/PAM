# Roadmap

What's deferred and the trigger that should bring each forward.

## Deferred — pinned, will land when triggered

### Outbox + transactional integration-event publishing
- **What**: MassTransit EF Core outbox on `PlayersDbContext`; flip
  `PlayerRegisteredDomainHandler` to publish `PlayerRegisteredIntegrationEvent`.
- **Trigger**: first cross-module consumer (Audit or Notifications module).
- **Why deferred**: no consumers yet, and direct publish without outbox would
  be inconsistent (event sent but DB write rolled back).
- **For Wallet, this is non-negotiable on day one of that module.**

### Audit module
- **What**: `Pam.Audits` module, append-only, subscribes to all integration
  events. Records actor, IP, correlation-id, raw event payload.
- **Trigger**: regulator asks, or first jurisdiction we operate in requires
  it (most do).
- **Note**: this is **regulatory** audit. Application audit (the
  `(CreatedByType, CreatedById, LastModifiedByType, LastModifiedById)`
  columns on every `Entity<TId>`) already covers app-level "who did
  what."

### Operators audience + admin endpoints
- **What**: second `JwtBearer` scheme in `Pam.Api` validating tokens issued
  by a dedicated ZITADEL audience for back-office traffic. `Operator.*`
  policies for back-office actions (`SuspendPlayer`, `SearchPlayers`, etc.).
  Admin UI (BlazorServer or React) bound to that audience.
- **Trigger**: customer support team needs to act on accounts.
- **Setup**: extend `infra/zitadel/bootstrap.sh` to also create an operators
  Project + audience under a dedicated Org (or under each brand Org,
  depending on the back-office shape). Add a second `AddJwtBearer("operators",
  ...)` block in `Program.cs`. Define `operator.support` /
  `operator.compliance` / `operator.admin` roles via ZITADEL roles or
  authorization grants.

### Distributed rate limiter
- **What**: replace the in-memory `auth-sensitive` policy with a Redis-backed
  one (`RedisRateLimiting` package or similar).
- **Trigger**: running multiple Pam.Api replicas. Until then, the in-memory
  limiter stacked with ZITADEL's brute-force protection is enough.

### OTLP exporter + collector
- **What**: add `.AddOtlpExporter()` on the OTel tracing/metrics builders;
  add an OpenTelemetry Collector to compose; route to Tempo/Loki/Prometheus.
- **Trigger**: production, or first time you need to debug a multi-service
  flow. Until then, instrumentation is collected and discarded.

### Reconciliation job for orphan ZITADEL users
- **What**: nightly job that lists ZITADEL users (per brand Org) with no
  matching PAM player and either deletes them or pages support.
- **Trigger**: when registration volume is non-trivial. The current
  best-effort `DeleteUserAsync` rollback in `RegisterPlayerHandler` covers
  the common case but has a tiny window where both the DB save and the
  ZITADEL delete can fail.

### CorrelationId middleware
- **What**: explicit `X-Correlation-Id` middleware that flows through
  MediatR + outbox + MassTransit headers.
- **Trigger**: the first cross-process flow that needs to be replayed for
  debugging. OTel `traceparent` covers HTTP↔HTTP today; CorrelationId is
  needed when async outbox flows span multiple traces.

### Production secret store
- **Status**: env-var-driven. Orchestrator (systemd / k3s / Swarm) injects
  secrets as env vars; ASP.NET maps `__` → `:` over `appsettings.{env}.json`.
  No in-process secret-fetching code. (An Infisical prototype was removed
  in `3b4d6e9` — see commit message.)
- **Open**: pick a dedicated secret store when there's a real production
  deploy target. Candidates: HashiCorp Vault (most flexible, heaviest ops),
  SOPS + age (file-based, GitOps-friendly, no server), k3s External Secrets
  (cleanest if we land on k3s).
- **Trigger**: first staging or production environment that needs more
  than orchestrator-managed env vars (rotation, audit, fine-grained access
  controls, multi-team isolation).

### Tests
- **Done**: pure-domain unit tests in `tests/Pam.Players.UnitTests/`
  covering `Player.Register` (age boundaries, brand carry-through, event
  raising) and the four value objects, on xUnit 3 + FluentAssertions 7.
- **What's left**: integration tests with Testcontainers (real Postgres,
  real ZITADEL) for the registration flow end-to-end; architecture tests
  via `NetArchTest.Rules` for module-boundary enforcement.
- **Trigger**: before module #2 lands. Architecture tests in particular
  pay for themselves the moment a second module makes it possible to
  cross-reference internal types.

## Smoke-test caveats found and tracked

### Redis + Seq compose port conflicts
`docker-compose.yml` declares both, but they default to standard ports
(`6379`, `5341`) which conflict with the user's `dmt-redis` and `dmt-seq`
peer-project containers. Decide whether to renumber ours or stop the others
when the API actually needs them (Redis: distributed rate limiting; Seq:
log viewing).

## Modules not yet built (initial cut from architecture discussion)

In priority order for a regulated sportsbook/casino PAM:

1. **Kyc** — verification state machine, document review, status transitions
   driving Player status. Needed before any real-money flow.
2. **Wallet** — double-entry ledger, deposits, withdrawals, holds, balance
   projections. Outbox required from day one. Most regulated module.
3. **Limits** — deposit/loss/session limits, cooling-off, self-exclusion. Hooks
   into the MediatR pipeline as a `LimitsBehavior` so any
   `IPlayerInitiated` command is checked before the handler.
4. **Bonus** — grants, wagering progress, expiry. Subscribes to wallet events.
5. **Notifications** — email/SMS/push. Subscribes to Player/KYC/Wallet events.
6. **Operator** — back-office user management (when the operator audience
   and admin endpoints land).
7. **Audit** — append-only, subscribes to everything (see above).

Game wallet ingress (`Pam.GameWallet`) is a **separate host**, not a module.
Game providers hit it with HMAC-signed `/auth`, `/bet`, `/win`, `/rollback`
calls; latency budget is sub-200ms p99. It calls into `Pam.Wallets` via the
contracts interface but does not share the player-API request pipeline.
