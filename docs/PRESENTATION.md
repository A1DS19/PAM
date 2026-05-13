# PAM — Modular architecture POC

Presentation for the team — 2026-05-12, the day after the BA Core
Platform review meeting. Each `---` block is one slide; copy the title +
body straight into Keynote / Google Slides / Powerpoint. The `>` blocks
are speaker notes — don't put those on the slide.

---

## Slide 1 — Title

**PAM: a modular foundation for the BA Core Platform**

Jose Padilla · May 12 2026

> Open with: "yesterday we agreed on a June 1 decision date and on
> running parallel POCs. This is mine. I built a working prototype that
> validates the modular architecture I proposed. It's running locally
> and we'll demo it." 90 seconds.

---

## Slide 2 — The proposal in one paragraph

> A **modular monolith** today, **microservice-ready** when scale or
> team-org demands it. One deploy, one SQL Server, zero distributed-system
> tax — but with the module boundaries already drawn so any module can
> be extracted later with a mechanical refactor, not a rewrite.

Three structural rules, repeated everywhere:

1. **Schema-per-module.** Each module owns its tables under its own
   SQL Server schema (`identity.*`, `operators.*`, `audit.*`, …). No
   cross-schema joins.
2. **`*.Contracts` is the only seam.** A module can be referenced from
   another module *only* via its `Contracts` assembly. Direct project
   references are blocked at the architecture-test layer.
3. **Modules communicate via events, not method calls.** Integration
   events on RabbitMQ for "what happened"; in-process queries via
   `IQuery<T>` from `Contracts` for "give me this data". No module ever
   reads another module's DbContext.

> If we follow these rules, any module can be extracted to its own
> service in the future by replacing its `IQuery<T>` implementation
> with a gRPC client. Same interface, different transport.

---

## Slide 3 — What's already built

Five modules, in production shape or scaffolded:

| Module | What it does | Status |
|---|---|---|
| `Pam.Operators` | Brand aggregate + multi-tenant foundation | shipped |
| `Pam.Identity` | OAuth/OIDC, users, MFA, roles, permissions | shipped (3 PRs) |
| `Pam.Notifications` | SMTP gateway + email templates | shipped (skeleton) |
| `Pam.Audit` | Every back-office command logged, append-only | shipped |
| `Pam.Players` | Players aggregate, KYC, sessions | scaffolded |
| `Pam.Wallet` | Double-entry ledger | scaffolded (outbox-ready) |

83 tests across 6 suites. Build passes. Multi-replica safe. Full
observability over OTLP.

> Audit was something I proposed yesterday in the meeting; it shipped
> today. That's the velocity advantage of having the foundation already
> in place — features land in days, not weeks.

---

## Slide 4 — Per-module pattern, in code

Three csproj per module, exactly the same shape every time:

```
src/Modules/<X>/
  Pam.<X>/                    aggregates, features, EF, wire-up (private)
  Pam.<X>.Contracts/          DTOs, IQuery<T>, integration events (public)
tests/
  Pam.<X>.UnitTests/          aggregate + validator tests
```

Inside each module:

```
<Aggregate>/
  Models/<Aggregate>.cs
  Features/<UseCase>/
    <UseCase>Command.cs       what the API receives
    <UseCase>Validator.cs     FluentValidation rules
    <UseCase>Handler.cs       the actual work
    <UseCase>Endpoint.cs      Carter route registration
  Events/<DomainEvent>.cs     in-module fact
  EventHandlers/              bridge domain → integration
```

> Adding a new feature is "copy the four files, rename, fill in." We
> have a working template in `Pam.Operators/Brands/Features/CreateBrand`.
> Every new feature is mechanical from there. This is what makes
> Roger's incremental delivery actually incremental.

---

## Slide 5 — Identity is end-to-end shipped

`Pam.Identity` is the proof that this scales past "scaffold + one
feature." It runs:

- OAuth 2.0 / OIDC server (OpenIddict, embedded — no separate IDP host)
- Authorization Code + PKCE for the SPA, Refresh-token rotation
- ASP.NET Core Identity for user storage, password hashing, lockout
- TOTP MFA enroll/verify, recovery codes, admin reset
- Forgot password + reset + email confirmation flows
- Token revocation that propagates in seconds
- Roles (Owner/Manager/Operator/Accountant) + fine-grained Permissions
- Bootstrap Owner from env vars on first run

~20 endpoints. 52 unit tests. Documented end-to-end in `docs/AUTH.md`.

> Identity is the hard module — the one most projects underestimate. We
> already shipped it. That removes the biggest risk on the critical
> path for everything downstream (CS Part 1, master player table, full
> player website).

---

## Slide 6 — Events: domain vs integration

```
┌────────────────────────────────────────────────────────────┐
│ Pam.Operators (one module)                                 │
│                                                            │
│  Brand.Create() raises BrandCreatedDomainEvent             │
│         │                                                  │
│         ▼ in-process, same transaction                     │
│  BrandCreatedDomainHandler                                 │
│         │                                                  │
│         ▼ publishes (via outbox)                           │
│  BrandCreatedIntegrationEvent                              │
└─────────────────────┬──────────────────────────────────────┘
                      │
                  (RabbitMQ)
                      │
   ┌──────────────────┼────────────────────┐
   ▼                  ▼                    ▼
 Notifications     Audit subscriber    future modules
 (welcome email)   (cross-cutting)
```

| | Domain event | Integration event |
|---|---|---|
| Scope | One module only | Across modules |
| Lives in | `Pam.<X>/Events/` | `Pam.<X>.Contracts/IntegrationEvents/` |
| Public contract? | No — refactor freely | Yes — versioned |
| Transport | MediatR in-process | RabbitMQ via outbox |

> The rule from `ARCHITECTURE.md`: **integration events describe what
> happened, not what to do.** Subscribers decide. That's what keeps
> modules decoupled. We bake this into code review and into the
> architecture tests — types in `*.IntegrationEvents.*` must end with
> `IntegrationEvent`, and `Pam.<X>` can't reference `Pam.<Y>` directly.

---

## Slide 7 — Outbox: events survive crashes

The MassTransit EF Core outbox is wired on every publishing module's
DbContext. Domain handlers run **pre-save**, inside the same DB
transaction as `SaveChanges`. `IPublishEndpoint.Publish` writes to
`messaging.outbox_message` (queued during the business transaction,
committed at the tail of the command pipeline). A background delivery
service forwards to RabbitMQ. See DECISIONS.md ADR #26 for why we own
one shared messaging schema instead of per-module outbox tables.

```
Business SaveChanges transaction
  ├── INSERT INTO operators.brands         ← aggregate row
  └── COMMIT                                ← business write persisted

                       │  (publish queued into PamMessagingDbContext
                       │   change tracker by MT's bus-wide outbox)
                       ▼
OutboxFlushBehavior — tail of command pipeline
  ├── INSERT INTO messaging.outbox_message ← integration event
  └── COMMIT                                ← outbox row persisted
                       │
                       ▼ BusOutboxNotification (immediate, async)
              EntityFrameworkOutboxDeliveryService
                       │
                       ▼
                  RabbitMQ exchange
```

> What this buys us: once the outbox row is committed, a crash before
> broker publish can never lose the event — the row in
> `messaging.outbox_message` is the source of truth and the delivery
> service retries until the broker accepts it. The business commit
> and the outbox commit are still separate transactions (ADR #28
> documents the failed attempt at single-txn atomicity via shared
> `SqlConnection`/`UseTransaction` — EF Core's connection model
> fights it). What closes the practical gap is the
> `OutboxReconciliationService` + per-module `IOutboxReconciler` +
> `outbox_dispatched_log` table: every published event leaves a log
> row, and the reconciler republishes any business row whose log
> entry is missing. **Non-negotiable for Wallet** — the reconciler
> covers Wallet from day one of the ledger.

---

## Slide 8 — Multi-replica safe today

Five concrete things that have to be true before we can run N replicas
behind a load balancer:

| Concern | Solution |
|---|---|
| Cookies issued by replica A validate on replica B | DP master keyring persisted to `identity.data_protection_keys` |
| OpenIddict signing/encryption keys shared | PFX files mounted into every replica, rotated by swapping the file |
| Rate-limit counts are global, not per-instance | Redis-backed sliding/fixed window limiters (RedisRateLimiting package) |
| Concurrent boot doesn't race on UNIQUE constraints | `sp_getapplock(100_001)` wraps the seeder |
| Async flows correlate across services | `CorrelationIdMiddleware` propagates through Activity baggage → MassTransit → outbox |

> All five are shipped. The system can run with two replicas behind a
> load balancer today. We don't have load balancing wired in dev, but
> nothing in the code stops it.

---

## Slide 9 — Audit logging (yesterday's commitment)

Every back-office command, automatically logged.

A MediatR pipeline behavior catches every `ICommand` execution and writes
to `audit.command_log`:

- `correlation_id` (W3C activity id, propagated via header)
- `actor_type` + `actor_id` (Owner/Manager/Operator/System/Anonymous)
- `request_type` (the C# command type name)
- `payload_json` (`nvarchar(max)` JSON, with sensitive fields redacted; SQL Server's JSON path operators query it)
- `started_at` / `completed_at` / `duration_ms`
- `status` (Success / Failure) + error type/message on failure

```sql
SELECT actor_id, request_type, status, started_at
FROM audit.command_log
WHERE actor_id = $1
ORDER BY started_at DESC LIMIT 100;
```

Index-served on `(actor_id, started_at DESC)`.

> Built end-to-end overnight. The redactor pattern means we never log
> raw passwords / tokens, even when the command carries them. Failure
> in audit never fails the user-facing command — the audit service
> swallows its own errors and logs a warning. That's the right
> trade-off: missing an occasional audit row is better than making
> audit a SPOF.

---

## Slide 10 — Test gate

83 tests across 6 suites:

| Suite | Tests | What it covers |
|---|---|---|
| Operators unit | 18 | Brand aggregate + validator |
| Identity unit | 52 | 9 validators + PermissionResolver |
| Players unit | 1 | scaffold |
| Wallet unit | 1 | scaffold |
| **Architecture** | 8 | module boundary + event shapes (NetArchTest) |
| **Integration** | 3 | real SQL Server + RabbitMQ + Redis (Testcontainers) |

> Architecture tests are the interesting ones. They enforce in CI
> that `Pam.Operators` can't reference `Pam.Identity` directly — only
> `Pam.Identity.Contracts`. The boundary rule from the architecture
> doc is automated; no one can accidentally break it. Integration
> tests boot the actual host against real SQL Server/Rabbit/Redis
> containers via Testcontainers, so we catch DI graph issues and
> migration problems before they hit dev.

---

## Slide 11 — Observability

Traces, metrics, logs leave the process over OTLP. Local dev runs the
`grafana/otel-lgtm` all-in-one — Tempo + Mimir + Loki + Grafana on
`:3001`.

Instrumentation sources tagged:
- ASP.NET Core (every request → span)
- HttpClient (outbound calls)
- EF Core (every query → span with SQL)
- `Microsoft.Data.SqlClient` (the driver itself)
- MassTransit (publishes + consumes)
- `Pam.*` ActivitySources (any `using var activity = …` we add)

Resource attributes on every signal: `service.name`, `service.version`,
`deployment.environment`, `host.name`.

> Production swaps the OTLP endpoint via env var. Pick Tempo+Loki on
> Grafana Cloud, or self-hosted, or Honeycomb, or Datadog — no code
> change. This is what makes "why is request X slow" answerable in
> production.

---

## Slide 12 — Caching

Redis read-through cache, opt-in via attributes on `IQuery<T>`.

```csharp
[Cache(durationMinutes: 5, keyPattern: "identity:user:{UserId}")]
public sealed record GetUserQuery(Guid UserId) : IQuery<BackOfficeUserDto>;

[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record UpdateUserCommand(...) : ICommand;
```

The MediatR `CachingBehavior` intercepts cacheable queries, hits Redis
first, falls through to the handler on miss. Commands carrying
`[InvalidateCache]` purge matching patterns after success.

`Pam.Identity / Users` is the first adopter — `GetUserQuery` and
`ListUsersQuery`.

> Cache is opt-in per query, not implicit on every read. We pick which
> reads matter; we don't blanket-cache. Failure mode: Redis blip →
> the read goes through to the handler, no error surfaced. Caching is
> a performance feature, not a correctness one.

---

## Slide 13 — Live demo

We'll spend 10–12 minutes in the terminal + browser:

1. `make up` — bring up SQL Server + RabbitMQ + Redis + Mailpit + LGTM
2. `make dev-api` — start the API (no other services need to start; LGTM is for show)
3. **Authentication**: `POST /v1/identity/login` → cookie + access token via OIDC PKCE
4. **MFA**: enroll TOTP → verify → login flow now requires both factors
5. **Authz**: `POST /v1/operators/brands` (gated on `Permissions.operators.brands.write`) → 403 without perm; assign permission → 201
6. **Audit**: SELECT the rows in `audit.command_log` — every command we just ran is there
7. **Scalar UI**: `/scalar/v1` — the API is self-documenting
8. **Grafana LGTM**: jump to the Explore tab — traces from the demo requests are visible
9. **Architecture tests**: `dotnet test tests/Pam.ArchitectureTests/` — boundary rules enforced

> If anyone questions whether this is "real" or "production-shaped",
> the demo shows it: real auth, real DB, real broker, real telemetry,
> real audit. Everything Roger's requirements asked for, with one
> caveat — see next slide.

---

## Slide 14 — Honest gaps

What PAM **doesn't** do yet, with the trigger to land each:

| Missing | Where it lives | Trigger to build |
|---|---|---|
| **CS Part 1 transaction-intercept layer** (casinos, lottos, 3rd-party SBs, horses, cashier) | not started | top priority post-Jun 1 |
| `Pam.Players` real domain (registration, search, transaction history) | scaffolded | post-Jun 1, after CS Part 1 starts |
| `Pam.Wallet` real ledger (double-entry, balance snapshots, multi-currency) | scaffolded | post-Players, before Listo |
| `Pam.Affiliates` agent system | not started | CS Part 2 |
| Sync jobs to GBS player table (DGS-pattern) | not designed | CS Part 1 / CS Part 2 cutover |
| `gbs-db` strangler-fig migration plan | not written | needed before any phase routes traffic away from GBS |
| SQL Server-vs-SQL-Server justification (CTO doc asked) | not written | this week |
| Brand-scoped EF global query filter | placeholders in DbContexts | with first authenticated Player endpoint |
| Auth-aware integration tests | harness ready | next test PR |

> Nothing in the gap list is *architectural* — the foundation handles
> all of it. The work to close each gap is module-level code on top of
> what's already running. Estimating: CS Part 1 first deliverable
> (one provider type, casino) ~3 weeks once we commit. Master player
> table migration ~2–3 weeks. Affiliate system ~2 weeks.

---

## Slide 15 — Mapping to Roger's phase diagram

```
Roger's diagram (PDF page 10)             PAM module / status
─────────────────────────────────         ──────────────────────────
Website 2.0                               (frontend; Figma → SPA)
   │                                       │
   ▼                                       ▼
CS Unified Transaction View       ←→     Pam.Ingest (NEW — biggest gap)
   ├──→ Simplified Fin Reporting          (consumer of Pam.Ingest stream)
   ├──→ Casino Cash Bonus                 Pam.Bonuses (new)
   └──→ CS Remaining Features    ←→     Pam.Players (scaffolded today)
                  │                              │
                  ▼                              │
        Migrate Player Wallet      ←→     Pam.Wallet (scaffolded, outbox-ready)
                  │                              │
                  ▼                              ▼
        Non-GBS Sports Adjustment  ←→     Pam.Adjustments (could live in Wallet)
                  │
                  ▼
        Accurate Financial Reports        Pam.Reporting (deferred)
                  │
                  ▼
                Listo                     (no PAM module — the goal)
```

The two modules holding the bulk of the data (`Players`, `Wallet`) are
scaffolded with their schemas, DbContexts, and (for Wallet) outbox
already in place. Adding real domain code is a feature-by-feature push,
not a foundation-and-then-features one.

> What Roger called "CS Part 1: Unified Transaction View" is the only
> module we haven't started. That's intentional — Brand and Identity
> were the unblocking modules and they had to come first. CS Part 1 is
> next on the ROADMAP if June 1 picks internal.

---

## Slide 16 — If we proceed, here's month 1–3

| Month | Deliverable | Why |
|---|---|---|
| **Month 1** (June) | `Pam.Ingest` POC — one provider type (casino), webhook ingest, normalized `Transaction` row, forward to GBS, emit integration event | Proves CS Part 1 end-to-end on real data |
| | `MIGRATION.md` design doc | Locks the strangler-fig story before any cutover begins |
| | Architecture ADRs: SQL Server-vs-SQLServer, .NET-vs-{Go,Rust,JS}, RabbitMQ-vs-Kafka | Answers the CTO doc's open questions |
| **Month 2** (July) | `Pam.Ingest` extended to lotto + 3rd-party SB + horses + cashier | Closes Roger's CS Part 1 spec |
| | `Pam.Players` real domain (registration with agent/affiliate, search, transaction history) | CS Part 2 |
| | Sync job: PAM → GBS player table (DGS-pattern) | Keeps GBS functioning during migration |
| **Month 3** (August) | `Pam.Affiliates` minimal — calculations move to PAM | CS Part 2 functional scope |
| | CS browser UI lights up against real `Pam.Players` + `Pam.Ingest` data | Customer service team gets the unified view |
| | Player website auth migration: passwords move into `Pam.Identity` under `pam_player_api` audience | Full Player Website phase begins |

> "Many months" from the CTO doc means ~9–12 of these months end-to-end
> before Listo. The first three are the ones that prove "internal route
> was the right call" — after that the trajectory is established.

---

## Slide 17 — IQsoft comparison (for the formal decision)

|  | Internal (this) | IQsoft |
|---|---|---|
| Time to first business value | ~4 weeks (CS Part 1 POC) | unknown; depends on their roadmap |
| Customization | unlimited — every line is ours | bounded by their product |
| Domain knowledge stays with us | Yes | No |
| Migration from GBS | Designed by us, controlled by us | Their integration pattern |
| Auth / identity / audit | already ours, configurable | their stack, configure-only |
| Per-brand multi-tenancy | designed in from day 1 (ADR #14) | depends on their model |
| Strangler-fig possible | Yes — Roger's phased plan fits | depends on their cutover story |
| License cost | engineering hours + infra | recurring license + integration |
| Worst-case | features take time we own | their roadmap changes |
| Best-case | A platform we own, indefinitely | A platform we license, indefinitely |

> This is the part where the team's informal preference makes sense:
> we have a real foundation, real velocity, and the trade is engineering
> hours vs vendor lock-in. The POC is what makes "real foundation" not
> just a claim.

---

## Slide 18 — The ask

By **June 1**, the team chooses between:

1. **Proceed internally**, with the modular foundation shown today
2. **IQsoft**

If (1), commit to the month 1–3 deliverables on slide 16, with squad
restructuring to colocate backend + frontend ownership (per yesterday's
meeting).

If (2), this POC stays as the audit/identity/event substrate; IQsoft
plugs in for the rest.

Either way, **the work between now and June 1**:

- **Me**: Pam.Ingest design sketch + the three architecture ADRs +
  `MIGRATION.md` first draft.
- **Rodolfo**: CS unified-transaction dashboard POC.
- **Milton**: Tokyo Web Admin demo for the team to review.
- **Roger**: confirm the gating items + share any additional documents.

> End on the ask explicitly. Don't hedge. The team has 20 days; the
> POC work makes the decision data-driven instead of a feeling.

---

## Appendix A — Repo / docs map (for follow-ups after the meeting)

```
docs/
  ARCHITECTURE.md           the why behind every design choice
  DECISIONS.md              ADRs, append-only — 20 entries
  ROADMAP.md                trigger-gated punch list of what's next
  AUTH.md                   Identity module reference (every endpoint)
  CACHING.md                Redis cache pattern + opt-in usage
  TESTING.md                test suite inventory + conventions
  PLATFORM_HARDENING.md     the 38→62 hardening sweep
  CORE_PLATFORM_MAPPING.md  legacy feature surface → PAM module map
  LOCAL_DEV.md              first-time setup, smoke test, makefile
  PRESENTATION.md           this deck

src/
  Bootstrapper/Pam.Api/     the host (Program.cs)
  Modules/<X>/Pam.<X>/      module implementation (private)
  Modules/<X>/Pam.<X>.Contracts/  module public surface
  Shared/Pam.Shared/        DDD primitives, behaviors, interceptors
  Shared/Pam.Shared.Contracts/  cross-cutting interfaces (IQuery, IClock, IAuditService, ...)
  Shared/Pam.Shared.Messaging/  MassTransit registration

tests/
  Pam.<X>.UnitTests/        per-module aggregate + validator tests
  Pam.ArchitectureTests/    NetArchTest module-boundary rules
  Pam.IntegrationTests/     Testcontainers — real SQL Server/RabbitMQ/Redis
```

## Appendix B — Likely questions

> Anticipate these. Have a one-liner for each ready.

**"Why SQL Server (and not Postgres)?"**
ADR #27 — picked on operational fit, not technical merit. IT already
operates SQL Server in production (runbooks, backup chain, DR rehearsal,
on-call rotation); Postgres would be a green-field operational
introduction with no existing on-call. Peer alignment with GBS reduces
the migration surface: schema-per-schema cutover stays in-engine, no
cross-RDBMS conversion. ADR #22 (which originally picked Postgres on
technical grounds — JSON path, snake_case, partitioning) is left as
SUPERSEDED so the technical trade-offs stay on the record.

**"Why .NET and not Go or JavaScript?"**
Type system fits regulated finance, hiring pool in LATAM is large, the
team is .NET-native. Go/Rust/Elixir become candidates only for the
live-betting hot loop (separate service), not for back-office.

**"How long until CS Part 1 actually replaces something in production?"**
A working POC (one provider type, no production traffic) in ~3 weeks.
First production cutover for that provider ~6–8 weeks after that, depending
on QA + Soefe coordination. Then each subsequent provider is ~1–2 weeks of
mechanical work.

**"What if we hit a regulator-driven feature we hadn't planned for?"**
The modular pattern absorbs it. Add a module or extend an existing one.
The architecture doesn't constrain feature scope — it constrains how
features are added (clean boundaries, events, no cross-schema reads).

**"What's the risk of the modular monolith staying a monolith forever?"**
None — that's the *point*. We don't pay distributed-system cost until we
need to. If we never need to, we never pay it. If we do (Wallet at scale,
GameWallet ingress), the contracts assembly is the seam; extraction is
mechanical.

**"Is there a CI/CD pipeline?"**
Not yet — `make test` is the only gate. Pinned in ROADMAP; trigger is
the first staging environment.

**"Can the back-office team see this today?"**
Yes — boot the API locally, open Scalar at `/scalar/v1`, every endpoint
is documented. We can also run it on a shared dev box if Roger wants the
team poking at it before June 1.

---

## Speaker notes — pacing

- Slides 1–4: 5 minutes. The thesis + pattern.
- Slides 5–9: 8 minutes. What's shipped, including the audit module
  proof-of-velocity.
- Slides 10–12: 4 minutes. Tests + observability + caching — the
  "production-shaped" evidence.
- **Slide 13 — demo: 10–12 minutes.** This is the centerpiece. Make
  sure `make up && make dev-api` is already running before the meeting
  starts; reboot if anything went wrong with overnight upgrades.
- Slides 14–15: 5 minutes. Honest gaps, then map onto Roger's diagram.
- Slides 16–18: 7 minutes. Month 1–3, IQsoft comparison, the ask.

Total: ~40 minutes presentation + Q&A. If running short, cut Slide 11
(Observability — fold into the demo) and Slide 12 (Caching — mention
in passing).
