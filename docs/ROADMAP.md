# Roadmap

What's deferred and the trigger that should bring each forward.

## Up next — ordered punch-list

The current focus is Phase 3, then Players returns. Each item links to a
detail section further down for the *what* and *why deferred*.

1. **Phase 3 — `Pam.Identity` (OpenIddict + ASP.NET Core Identity).**
   Embedded OAuth 2.0 / OIDC server, back-office Users & Agents, roles +
   permissions. See [`Pam.Identity`](#pamidentity-openiddict--aspnet-core-identity)
   below and ADR #16.
2. **Re-introduce `Pam.Players`** (now as module #3 under the new
   ordering). Player aggregate scoped under a `Brand`, authenticated via
   the embedded IDP issued in Phase 3. KYC + sessions + limits land as
   sub-aggregates of the same module.
3. **Test gate before module #4** — outbox + integration tests
   (Testcontainers: real Postgres) + arch tests (`NetArchTest.Rules`).
   See [Outbox](#outbox--transactional-integration-event-publishing) and
   [Tests](#tests).
4. **Module #4 — `Pam.Wallet`** (double-entry ledger). Outbox is
   non-negotiable from day one of that module. See
   [the build order](#modules-not-yet-built) below.

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
  (only seeded if no Owner exists).
- Cookie-challenge redirect to the React SPA's `LoginUrl?returnUrl=…`
  for browser navigations; JSON / `/v1/*` requests get 401 instead.
- Quartz hosted service: hourly cleanup of orphaned tokens and
  authorizations.
- `Pam.Api` fallback authorization policy requires bearer auth via the
  OpenIddict validation scheme; one `Permissions.<code>` policy per
  permission code, with `platform.admin` granting every other policy
  via assertion.

**PR 2 — back-office user management + MFA. NEXT.**

All endpoints under `/v1/identity/...`, gated on the matching permission.

- `POST /v1/identity/users` — admin creates a back-office user.
  **Replaces "register"; back-office users are never self-service**, only
  Owner/Manager can add them. Permission: `identity.users.write`.
- `GET /v1/identity/users` / `GET /v1/identity/users/{id}` — list +
  detail. Permission: `identity.users.read`.
- `PATCH /v1/identity/users/{id}` — email, brand, status changes.
- `DELETE /v1/identity/users/{id}` — soft-delete (LockoutEnd = far
  future). Hard-delete violates regulatory retention.
- `POST /v1/identity/users/{id}/roles` /
  `DELETE /v1/identity/users/{id}/roles/{role}` — role assignment.
  Permission: `identity.roles.write`.
- `POST /v1/identity/users/{id}/unlock` — admin clears a lockout.
- `POST /v1/identity/me/change-password` — authenticated user changes
  their own password (knows the current one).
- `POST /v1/identity/me/mfa/enroll` + `/verify` — TOTP enrollment.
  Identity has this built in via `UserManager.GenerateAuthenticatorKey`
  + `VerifyTwoFactorTokenAsync`.
- `POST /v1/identity/login/mfa` — MFA challenge step. The login response
  shape `{ mfaRequired: true }` is already wired in PR 1.
- Apply `[Authorize(Policy = "Permissions.operators.brands.write")]` to
  the existing `CreateBrand` endpoint and read-policy to `GetBrand`.
- Phase 3 unit tests: `BackOfficeUser` aggregate behavior + role
  assignment + permission resolution against `PermissionResolver`.

**PR 3 — email-driven flows.**

Blocked on `Pam.Notifications` (or a stub email sender — TBD).

- `POST /v1/identity/forgot-password` — issues a token, sends a reset
  link via `Pam.Notifications`.
- `POST /v1/identity/reset-password` — consumes the token, sets new
  password.
- `POST /v1/identity/users/{id}/confirm-email` — completes email
  verification. Identity's defaults already require this if
  `SignIn.RequireConfirmedEmail = true` (currently false until PR 3).

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

### Outbox + transactional integration-event publishing

- **What**: MassTransit EF Core outbox on the publishing module's
  DbContext; flip the relevant `<Event>DomainHandler` to publish the
  integration event through the outbox.
- **Trigger**: first cross-module consumer (Audit, Notifications, or
  any module reacting to `BrandCreatedIntegrationEvent`).
- **Why deferred**: no consumers yet, and direct publish without outbox
  would be inconsistent (event sent but DB write rolled back).
- **For Wallet, this is non-negotiable on day one of that module.**

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

### Distributed rate limiter

- **What**: replace the in-memory `auth-sensitive` policy with a
  Redis-backed one (`RedisRateLimiting` package or similar).
- **Trigger**: running multiple Pam.Api replicas. Until then the
  in-memory limiter is enough — the embedded OpenIddict server's
  brute-force protection layers on top.

### OTLP exporter + collector

- **What**: add `.AddOtlpExporter()` on the OTel tracing/metrics
  builders; add an OpenTelemetry Collector to compose; route to
  Tempo/Loki/Prometheus.
- **Trigger**: production, or first time you need to debug a
  multi-service flow. Until then, instrumentation is collected and
  discarded.

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
  CreateBrand validator) on xUnit 3 + FluentAssertions 7. 18 tests,
  ~80ms run.
- **What's left**: integration tests with Testcontainers (real Postgres,
  real RabbitMQ) for the create-brand flow end-to-end; architecture
  tests via `NetArchTest.Rules` for module-boundary enforcement.
- **Trigger**: before module #4 (Wallet) lands. Architecture tests in
  particular pay for themselves the moment a second module makes it
  possible to cross-reference internal types — which is exactly when
  Identity ships.

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
