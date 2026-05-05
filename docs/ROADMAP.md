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
- **Note**: this is **regulatory** audit. Application audit (createdBy,
  modifiedBy) is already on every entity.

### Operators realm + admin endpoints
- **What**: second Keycloak realm `operators`, second `JwtBearer` scheme in
  `Pam.Api`, `Operator.*` policies for back-office actions
  (`SuspendPlayer`, `SearchPlayers`, etc.). Admin UI (BlazorServer or React)
  bound to the operators realm.
- **Trigger**: customer support team needs to act on accounts.
- **Setup**: drop `operators-realm.json` next to `players-realm.json`. Add a
  second `AddJwtBearer("operators", ...)` block. Define
  `operator.support`/`operator.compliance`/`operator.admin` realm roles.

### Distributed rate limiter
- **What**: replace the in-memory `auth-sensitive` policy with a Redis-backed
  one (`RedisRateLimiting` package or similar).
- **Trigger**: running multiple Pam.Api replicas. Until then, the in-memory
  limiter stacked with Keycloak's brute-force protection is enough.

### OTLP exporter + collector
- **What**: add `.AddOtlpExporter()` on the OTel tracing/metrics builders;
  add an OpenTelemetry Collector to compose; route to Tempo/Loki/Prometheus.
- **Trigger**: production, or first time you need to debug a multi-service
  flow. Until then, instrumentation is collected and discarded.

### Reconciliation job for orphan Keycloak users
- **What**: nightly job that lists Keycloak users in the `players` realm
  with no matching PAM player and either deletes them or pages support.
- **Trigger**: when registration volume is non-trivial. The current
  best-effort `DeleteUserAsync` rollback in `RegisterPlayerHandler` covers
  the common case but has a tiny window where both the DB save and the
  Keycloak delete can fail.

### CorrelationId middleware
- **What**: explicit `X-Correlation-Id` middleware that flows through
  MediatR + outbox + MassTransit headers.
- **Trigger**: the first cross-process flow that needs to be replayed for
  debugging. OTel `traceparent` covers HTTP↔HTTP today; CorrelationId is
  needed when async outbox flows span multiple traces.

### Production secret store
- **What**: Vault or Infisical for runtime secrets (DB connection, Keycloak
  admin client secret, Rabbit credentials, Game-provider HMAC keys when
  GameWallet exists).
- **Trigger**: first non-dev environment. Local dev keeps secrets in
  `appsettings.json` and that's fine.

### Tests
- **What**: unit tests for domain (`Player.Register`, value-object
  validation), integration tests with Testcontainers (real Postgres, real
  Keycloak), architecture tests via NetArchTest (module-boundary
  enforcement).
- **Trigger**: before we have any module past Player. The user's call:
  "we won't test everything, only critical modules." Player is critical
  enough to back-fill once a few features stabilize.

## Smoke-test caveats found and tracked

### Keycloak User Profile schema declaration
Keycloak v25+ silently drops user attributes not declared in the realm's
user-profile schema. We currently use a one-shot post-import script
(`infra/keycloak/setup/declare-player-id.sh`) wired into `make up` to add
`player_id` to the `players` realm profile. Long-term: bake this into the
realm import format if Keycloak supports it natively in a later version, or
package it as a Keycloak SPI / config-cli step in the deployment pipeline.

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
6. **Operator** — back-office user management (when the operator realm and
   admin endpoints land).
7. **Audit** — append-only, subscribes to everything (see above).

Game wallet ingress (`Pam.GameWallet`) is a **separate host**, not a module.
Game providers hit it with HMAC-signed `/auth`, `/bet`, `/win`, `/rollback`
calls; latency budget is sub-200ms p99. It calls into `Pam.Wallets` via the
contracts interface but does not share the player-API request pipeline.
