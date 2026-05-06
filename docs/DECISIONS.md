# Decisions

A log of architectural decisions made during the project's life. Each entry
captures the **context** (why the choice came up), the **decision**, and the
**consequences** (what we accept by choosing this). Entries are
append-only — supersede rather than rewrite when a decision changes.

Rough ADR shape, kept terse. Order is reverse-chronological.

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
