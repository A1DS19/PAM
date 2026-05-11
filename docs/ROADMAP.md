# Roadmap

What's deferred and the trigger that should bring each forward.

## Up next — ordered punch-list

Phase 3 (`Pam.Identity`) is complete across three PRs. Next is Players
returning as module #3.

1. **Re-introduce `Pam.Players`** (module #3). Player aggregate scoped
   under a `Brand`, authenticated via the embedded IDP under a separate
   audience (`pam_player_api`). Self-service registration + KYC +
   sessions + limits land as sub-aggregates of the same module.
2. **Test gate before module #4** — outbox + integration tests
   (Testcontainers: real Postgres) + arch tests (`NetArchTest.Rules`).
   See [Outbox](#outbox--transactional-integration-event-publishing) and
   [Tests](#tests).
3. **Module #4 — `Pam.Wallet`** (double-entry ledger). Outbox is
   non-negotiable from day one of that module. See
   [the build order](#modules-not-yet-built) below.
4. **`Pam.Notifications` expansion** — module skeleton +
   `IEmailSender` are in place (`Pam.Notifications` +
   `Pam.Notifications.Contracts`). Still to come: integration-event
   consumers (the `Consumers/` folder is empty, waiting for Players to
   publish), templating + locale resolution, send-audit-log DbContext,
   SMS + push transports. Each piece lands when the first consumer
   needs it. See *Notifications and cross-module email* in
   ARCHITECTURE.md for the pattern.

## Deferred — pinned, will land when triggered

### `Pam.Identity` (OpenIddict + ASP.NET Core Identity)

Embedded auth module. OpenIddict `AddServer` configured for Authorization
Code + PKCE + Refresh (Client Credentials wired but not yet granted to any
client). ASP.NET Core Identity for `BackOfficeUser`, roles, permissions.
Single `IdentityDbContext` (schema `identity`) hosting Identity tables,
OpenIddict tables, and the custom `permissions` / `role_permissions` join.
`AddValidation().UseLocalServer()` in the same host — module endpoints
validate tokens in-process, no introspection hop.

Phase 3 ships across three PRs.

**PR 1 — OIDC plumbing + login + bootstrap. SHIPPED.**

- `POST /v1/identity/login` (cookie-based, JSON, anonymous, rate-limited
  5/min) — `SignInManager.PasswordSignInAsync` with lockout-on-failure.
- `GET/POST /connect/authorize` — Authorization Code + PKCE,
  `ConsentTypes.Implicit` for the back-office SPA (no consent UI).
- `POST /connect/token` — code exchange + refresh-token grant, with the
  claim set refreshed from the live user on every issuance.
- `GET/POST /connect/userinfo` — standard OIDC userinfo gated on scope.
- `GET/POST /connect/logout` — clears the Identity cookie + OpenIddict
  end-session (redirect to `post_logout_redirect_uri`).
- `POST /connect/par` — Pushed Authorization Requests handled natively.
- Token issuance projects `sub`, `email`, `name`, `preferred_username`,
  `role[]`, `pam_permission[]`, `brand_id` (when set). Destinations split
  between access and identity tokens by scope; `pam_permission` /
  `brand_id` are access-token-only; security stamp never leaves the
  cookie.
- Idempotent seeder: permissions (from `PermissionCodes.All`), roles
  (Owner / Manager / Operator / Accountant), role-permission grants
  (`PermissionCodes.RoleDefaults.*`), `pam_api` scope, the `pam-bo` SPA
  application descriptor (public client, PKCE required, redirect URIs
  from `Identity:BackOfficeSpa` config), and the bootstrap Owner from
  `PAM_BOOTSTRAP_OWNER_EMAIL` / `PAM_BOOTSTRAP_OWNER_PASSWORD` env vars
  (only seeded if no Owner exists). Wrapped in a Postgres
  `pg_advisory_xact_lock` (key 100_001) so concurrent replica boots
  serialise through the seed and can't race UNIQUE constraints.
- Cookie-challenge redirect to the React SPA's `LoginUrl?returnUrl=…`
  for browser navigations; JSON / `/v1/*` requests get 401 instead.
- Quartz hosted service: hourly cleanup of orphaned tokens and
  authorizations.
- `Pam.Api` fallback authorization policy requires bearer auth via the
  OpenIddict validation scheme; one `Permissions.<code>` policy per
  permission code, with `platform.admin` granting every other policy
  via assertion.

**PR 2 — back-office user management + MFA. SHIPPED.**

All endpoints under `/v1/identity/...`, gated on the matching permission.

- `POST /v1/identity/users` — admin creates a back-office user.
  Back-office users are never self-service; only Owner/Manager can add
  them. Permission: `identity.users.write`.
- `GET /v1/identity/users` (paged: `page`, `pageSize`, optional
  `brandId` / `role` / `lockedOut`) and `GET /v1/identity/users/{id}`.
  Permission: `identity.users.read`.
- `PATCH /v1/identity/users/{id}` — email, brandId, lockoutEnabled.
  Status flags (EmailConfirmed, TwoFactorEnabled) mutate through their
  own endpoints because they carry side effects.
- `DELETE /v1/identity/users/{id}` — soft-delete via `LockoutEnd =
  DateTimeOffset.MaxValue` + security-stamp rotation. Hard-delete
  violates regulatory retention.
- `POST /v1/identity/users/{id}/roles` /
  `DELETE /v1/identity/users/{id}/roles/{role}` — idempotent. Each
  change rotates the security stamp so token claim sets refresh.
  Permission: `identity.roles.write`.
- `POST /v1/identity/users/{id}/unlock` — clears the lockout end +
  resets failed-access count; refuses on soft-deleted users.
- `POST /v1/identity/me/change-password` — authenticated user changes
  their own password (knows the current one). Rate-limited.
- `POST /v1/identity/me/mfa/enroll` returns the base32 shared key + an
  `otpauth://` URI for QR rendering. `POST /v1/identity/me/mfa/verify`
  verifies the TOTP and flips `TwoFactorEnabled`.
- `POST /v1/identity/login/mfa` — MFA challenge step using
  `SignInManager.TwoFactorAuthenticatorSignInAsync`.
- `CreateBrand` and `GetBrand` now require
  `Permissions.operators.brands.write` and `Permissions.operators.brands.read`
  respectively (no more `.AllowAnonymous()`).
- `HttpUserContext` replaces the default `SystemUserContext` so audit
  columns reflect the authenticated operator's OIDC `sub` (with fallback
  to `Actor.System` for startup work / `Actor.Anonymous` for anonymous
  endpoints).
- Tests: validator suites for CreateUser / ListUsers / ChangePassword /
  MfaVerify, plus `PermissionResolver` against the EF Core in-memory
  provider. 33 Identity tests + 18 Operators tests, all green.

**PR 3 — email flows + MFA durability + revocation tightening. SHIPPED.**

Bundled three things: the ROADMAP-planned email flows, plus two pieces
that proved necessary once MFA was actually in use (recovery + admin
reset), plus the validation-stack hardening that makes revocation feel
instant.

- `POST /v1/identity/forgot-password` (anonymous, anti-enumeration: always
  204) — issues `UserManager.GeneratePasswordResetTokenAsync` and sends
  the reset link via `IEmailSender` only when the email exists and is
  confirmed.
- `POST /v1/identity/reset-password { email, token, newPassword }` —
  consumes the token, sets the new password. Rotates the security stamp
  so sessions on other devices die.
- `POST /v1/identity/confirm-email { email, token }` — anonymous; user
  clicks the link from email and posts back.
- `POST /v1/identity/users/{id}/send-confirmation-email` — admin re-send
  (gated on `identity.users.write`). Auto-fires on `POST /v1/identity/users`
  creation; SMTP failure is logged, not fatal — admin can retry.
- `POST /v1/identity/me/mfa/recovery-codes` — generates 10 one-time
  codes via `GenerateNewTwoFactorRecoveryCodesAsync`. Plaintext returned
  once; previously-issued codes get invalidated on regenerate.
- `POST /v1/identity/login/recovery-code { code }` — completes the
  partial sign-in using a recovery code instead of a TOTP (one-time use).
- `POST /v1/identity/me/mfa/disable { currentPassword }` — self-disable
  with password challenge; clears the authenticator key so re-enable
  starts fresh.
- `POST /v1/identity/users/{id}/mfa/reset` — admin reset (gated on
  `identity.users.write`). Clears authenticator, disables 2FA, rotates
  security stamp.
- Email sender: `IEmailSender` + `SmtpEmailSender` (MailKit) targeting
  the new `pam-mailpit` dev container at `localhost:1025`. `Identity:Smtp`
  config block carries host/port/credentials/from-address — production
  swap is a config change.
- OpenIddict validation now uses
  `EnableAuthorizationEntryValidation()` + `EnableTokenEntryValidation()`
  — revocation propagates immediately instead of waiting for the
  security-stamp validation window. `/connect/revocation` and
  `/connect/introspect` URIs registered on the server; the back-office
  SPA descriptor gets revocation+introspection endpoint permissions.

**Deferred indefinitely**

- Self-service back-office registration. Wrong threat model — anyone
  with `POST /v1/identity/users` is a privilege-boundary breach. The
  bootstrap Owner is the on-ramp.
- Dynamic client registration (`/connect/register`). We own all the
  clients; only `pam-bo` exists today, future module-to-module clients
  are seeded.

**Player auth — Phase 4, not Phase 3.**

`POST /v1/auth/register` (public, self-service) lands when the Players
module returns. Different scope set, different audience under the same
OpenIddict server.

**Reference patterns**: `pro-cr-billing`'s `User : IdentityUser<Guid>`
shape (lockout, password policy) carried over; `pro-cr-billing`'s custom
JWT + RefreshToken rotation NOT carried over (OpenIddict's token storage
replaces it).

### Outbox + transactional integration-event publishing — SHIPPED

MassTransit EF Core outbox is wired on `OperatorsDbContext`. Integration
events published from inside a `SaveChanges` scope
(`BrandCreatedDomainHandler` and equivalents) write to `outbox_message`
in the same transaction as the aggregate write; an
`EntityFrameworkOutboxDeliveryService` forwards to RabbitMQ.

`DispatchDomainEventsInterceptor` moved from post-save to pre-save so
the dispatch happens inside the transaction. See *Outbox + pre-save
domain-event dispatch* in ARCHITECTURE.md for the new semantics and
their trade-offs.

Per-module wiring: each publishing module exposes a
`ConfigureOutbox(IBusRegistrationConfigurator)` delegate, composed in
`Program.cs`'s call to `AddPamMassTransit`. Adding a new publisher
takes three steps (package, EF model entities, ConfigureOutbox).

**For Wallet, this stays non-negotiable on day one of that module.**

### Audit module

- **What**: `Pam.Audits` module, append-only, subscribes to all
  integration events. Records actor, IP, correlation-id, raw event
  payload with hash chaining.
- **Trigger**: regulator asks, or first jurisdiction we operate in
  requires it (most do).
- **Note**: this is **regulatory** audit. Application audit (the
  `created_by_type / created_by_id / last_modified_*` columns on every
  `Entity<TId>`) already covers app-level "who did what."

### Brand-scoped global query filters

- **What**: each `DbContext` adds an EF Core global query filter on
  `BrandId` so cross-brand reads are impossible by default once
  back-office endpoints arrive. Operator endpoints can elevate to
  cross-brand access via a specific permission
  (`operator.platform-admin`).
- **Trigger**: when the second brand-scoped aggregate (Player) lands.
  Adding the filter retroactively across N modules is N migrations and
  N code reviews; doing it once at module-#3-start is one PR.

### Shared signing material + DP keys — SHIPPED

Two pieces that have to be in place before a second replica can serve
traffic alongside the first:

- **Data Protection master keyring** persists to `identity.data_protection_keys`
  via `IDataProtectionKeyContext` on `IdentityDbContext`. Cookies +
  OpenIddict state strings issued by one replica validate on another.
  `SetApplicationName(...)` isolates keys across environments sharing a
  database (`pam-api/Development` ≠ `pam-api/Production`).
- **OpenIddict signing + encryption certificates** load from PFX files
  configured under `OpenIddict:{Signing,Encryption}Certificate:{Path,Password}`
  in non-Development. Mount the same PFX into every replica; rotate by
  swapping the file and rolling restarts. Development keeps the persisted
  self-signed certs from `AddDevelopment{Signing,Encryption}Certificate`.

Without either of these, tokens and cookies become per-instance state and
N>1 replicas can't share user sessions.

### Distributed rate limiter — SHIPPED

The `auth-sensitive` and `api-default` policies now use the
`RedisRateLimiting` package (cristipufu) instead of the in-memory
limiters. A single `IConnectionMultiplexer` registered as a DI singleton
backs both — `RedisFixedWindowRateLimiter` for `auth-sensitive`,
`RedisSlidingWindowRateLimiter` for `api-default`.

Failure mode: `AbortOnConnectFail=false` so a brief Redis outage doesn't
prevent boot, but rate-limited endpoints surface 5xx while Redis is
unreachable. Intentional — fail-open on `/login` is worse than a brief
outage. Health check (`identity-db`, `redis`) catches it.

Sliding-window semantics changed slightly: the Redis impl uses a sorted
set keyed by request timestamp (textbook sliding window), not segmented
buckets, so `SegmentsPerWindow` is gone. More accurate, not a regression.

### OTLP exporter + collector — SHIPPED

Traces, metrics and logs now leave the API over OTLP. Local dev uses the
`grafana/otel-lgtm` all-in-one container (Tempo + Mimir + Loki + Grafana
+ Collector — Grafana UI on `:3001`, OTLP gRPC on `:4317`, HTTP on
`:4318`). Resource attributes (`service.name`, `service.version`,
`deployment.environment`, `host.name`) are emitted on every signal.

Instrumentation sources: AspNetCore + HttpClient + EF Core + Npgsql +
MassTransit + `Pam.*` ActivitySources/Meters. Logs flow through Serilog's
OpenTelemetry sink alongside the existing Console/Seq sinks.

Production swaps the endpoint via `OTEL_EXPORTER_OTLP_ENDPOINT` —
Grafana Cloud, self-hosted LGTM split out, Honeycomb, Datadog, …

### CorrelationId middleware

- **What**: explicit `X-Correlation-Id` middleware that flows through
  MediatR + outbox + MassTransit headers.
- **Trigger**: the first cross-process flow that needs to be replayed
  for debugging. OTel `traceparent` covers HTTP↔HTTP today;
  CorrelationId is needed when async outbox flows span multiple traces.

### Architecture tests

- **What**: `NetArchTest.Rules` test project (or a small project at the
  repo root) enforcing the per-module boundary: a `Pam.<X>` project
  cannot reference a `Pam.<Y>` project directly (only `*.Contracts`).
- **Trigger**: as soon as a second non-Identity module exists. Without
  enforcement the rule rots within a few PRs.

### Production secret store

- **Status**: env-var-driven. Orchestrator (systemd / k3s / Swarm)
  injects secrets as env vars; ASP.NET maps `__` → `:` over
  `appsettings.{env}.json`. No in-process secret-fetching code.
- **Open**: pick a dedicated secret store when there's a real
  production deploy target. Candidates: HashiCorp Vault (most flexible,
  heaviest ops), SOPS + age (file-based, GitOps-friendly, no server),
  k3s External Secrets (cleanest if we land on k3s).
- **Trigger**: first staging or production environment that needs more
  than orchestrator-managed env vars (rotation, audit, fine-grained
  access controls, multi-team isolation).

### Tests

- **Done**: unit tests for `Pam.Operators` (Brand aggregate behavior +
  CreateBrand validator) and `Pam.Identity` (validators for CreateUser /
  ListUsers / ChangePassword / MfaVerify / MfaDisable / ForgotPassword /
  ResetPassword / ConfirmEmail / LoginRecoveryCode + `PermissionResolver`
  against the EF Core in-memory provider). 70 tests on xUnit 3 +
  FluentAssertions 7, ~1s run.
- **What's left**: integration tests with Testcontainers (real Postgres,
  real RabbitMQ) for the create-brand and login flows end-to-end;
  architecture tests via `NetArchTest.Rules` for module-boundary
  enforcement.
- **Trigger**: before module #4 (Wallet) lands. Architecture tests in
  particular pay for themselves now that a second module (Identity)
  exists and references back into `Pam.Identity.Contracts` from
  `Pam.Operators`.

### Legacy `gbs-db` cutover plan

- **What**: a written strangler-fig design covering (a) which endpoints
  route to new PAM vs legacy `GBS-BAS`; (b) data sync direction during
  cutover (one-way pull from `gbs-db` into PAM, or dual-write); (c)
  reconciliation strategy for balances/state that exist in both stores
  during overlap; (d) rollback story per cutover slice.
- **Trigger**: before any PAM module starts owning reads/writes for
  data that production currently serves from `gbs-db`. The 100k
  existing players, 357 tables, and 1,901 stored procs are a real
  migration project, not an afterthought.

## Modules not yet built

In dependency / priority order:

1. **`Pam.Identity`** (Phase 3, in flight) — back-office Users &
   Agents, OpenIddict, roles + permissions.
2. **`Pam.Players`** — Player aggregate scoped under a Brand. KYC,
   sessions, limits land as sub-aggregates of this module to start;
   spin out as separate modules when the surface justifies it.
3. **`Pam.Wallet`** — double-entry ledger, deposits, withdrawals,
   holds, balance projections. Outbox required from day one. Most
   regulated module.
4. **`Pam.Limits`** (if not already inside Players) — deposit / loss /
   session limits, cooling-off, self-exclusion. Hooks into the MediatR
   pipeline as a `LimitsBehavior` so any `IPlayerInitiated` command is
   checked before the handler.
5. **`Pam.Bonuses`** — grants, wagering progress, expiry. Subscribes to
   wallet events. Bonus engine via a typed expression DSL (see
   CORE_PLATFORM_MAPPING.md decision #3).
6. **`Pam.Bets`** — internet bets + bet shops; Wallet + GameCatalog
   dependencies.
7. **`Pam.GameProviders` / `Pam.GameCatalog`** — provider integrations,
   product catalog.
8. **`Pam.Affiliates`** — own admin panel, commissions, referral
   tracking.
9. **`Pam.Notifications`** — email/SMS/push. Subscribes to
   Player/KYC/Wallet events.
10. **`Pam.Reporting`** — read-side projections over events; can be
    incrementally added.
11. **`Pam.Audits`** — append-only, subscribes to everything (see
    above).

Game wallet ingress (`Pam.GameWallet`) is a **separate host**, not a
module. Game providers hit it with HMAC-signed `/auth`, `/bet`, `/win`,
`/rollback` calls; latency budget is sub-200ms p99. It calls into
`Pam.Wallet` via the contracts interface but does not share the
back-office API request pipeline.

## Smoke-test caveats

### Redis + Seq compose port conflicts

`docker-compose.yml` declares both, but they default to standard ports
(`6379`, `5341`) which conflict with the user's `dmt-redis` and
`dmt-seq` peer-project containers. Decide whether to renumber ours or
stop the others when the API actually needs them (Redis: distributed
rate limiting; Seq: log viewing).

### `mprocs.log` and `dotnet watch`

If `mprocs` writes its log into the project tree, `dotnet watch` may
treat that log as a file change and trigger a hot reload loop. Exclude
the log file from watch via `<Watch Remove="…/mprocs.log" />` in
`Directory.Build.props` or the Pam.Api csproj when this becomes
annoying.
