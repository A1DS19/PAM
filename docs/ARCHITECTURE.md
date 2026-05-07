# Architecture

This document captures the **why** behind the architectural decisions for the
PAM (Player Admin Manager). What the code does is in the code; this document
exists for the decisions a future engineer can't recover from reading source.

## Modular monolith, not microservices (yet)

We build as a single deployable that is **internally** structured like a set
of services. Each module lives under `src/Modules/<Module>/Pam.<Module>/` with
its own `DbContext`, schema (`<module>.*`), aggregates, and feature folders.

- A module can be extracted to its own service when scale or team-org demands
  it. The module boundary (assembly, schema, contracts) makes that mechanical.
- Until then, we get a single deployable, one Postgres backup, one connection
  pool, and zero distributed-system tax.

**Hard rule**: a module never reads another module's data or types directly.
The only way modules communicate is:

1. **Integration events** through MassTransit + RabbitMQ (default).
2. **In-process queries** through `IQuery<T>` interfaces in
   `Pam.<Module>.Contracts`.
3. Never via `DbContext` or direct project reference into `Pam.<Module>` from
   another module.

The contracts assembly is the seam. A future microservice extraction is
"replace the in-process implementation behind `IBrandLookup` with a gRPC
client" — same interface, different transport.

## Per-module pattern (established Phase 2)

Every module follows the same shape. `Pam.Operators` is the reference; copy
its scaffolding for module #2 and beyond.

**Three projects per module:**

- `Pam.<Module>.Contracts` — DTOs, `IQuery<T>` definitions, integration
  events. The only thing other modules and `Pam.Api` reference for
  cross-module reads.
- `Pam.<Module>` — aggregates, features (vertical slices), domain events,
  EF DbContext + migrations, module wire-up. Internal to the module.
- `tests/Pam.<Module>.UnitTests` — aggregate behavior + validator tests.
  No DB, no broker.

**Folder shape inside `Pam.<Module>`:**

```
<AggregateName>/
  Models/<Aggregate>.cs
  Features/<UseCase>/
    <UseCase>Command.cs
    <UseCase>Validator.cs
    <UseCase>Handler.cs
    <UseCase>Endpoint.cs
  Events/<DomainEvent>.cs
  EventHandlers/<DomainEvent>DomainHandler.cs   # bridges to integration event
  Exceptions/<Aggregate>Errors.cs
Data/
  <Module>DbContext.cs
  <Module>DbContextDesignTimeFactory.cs
  Configurations/<Aggregate>Configuration.cs
  Migrations/
<Module>Module.cs                                # AddXModule + UseXModuleAsync
```

**Module wire-up.** Static `<Module>Module` class with two extension
methods:

- `AddXModule(this IServiceCollection, IConfiguration)` — registers DbContext,
  interceptors, health checks.
- `UseXModuleAsync(this IServiceProvider)` — runs `db.Database.MigrateAsync()`
  on startup.

`AuditableSaveChangesInterceptor` and `DispatchDomainEventsInterceptor` (from
`Pam.Shared`) are registered per-module via `services.TryAddScoped<...>()` so
duplicate registrations across modules are idempotent.

**Persistence rules:**

- Schema-per-module via `modelBuilder.HasDefaultSchema("<module>")` and
  `npg.MigrationsHistoryTable("__EFMigrationsHistory", "<module>")`.
- Snake_case columns via the `EFCore.NamingConventions` package
  (`options.UseSnakeCaseNamingConvention()`). Apply on **both** the runtime
  DbContext options and the design-time factory, or `dotnet ef migrations add`
  scaffolds PascalCase column names.
- Enum columns persisted as string with `.HasConversion<string>().HasMaxLength(N)`.
  Keeps the column self-describing and makes adding values append-only at any
  ordinal position.
- Audit columns (`created_at`, `created_by_type`, `created_by_id`,
  `last_modified_*`) configured explicitly on every entity.

**Health check:** `services.AddHealthChecks().AddNpgSql(connectionString,
name: "<module>-db", tags: ["ready", "module:<module>"])`.

## Identity: embedded OpenIddict + ASP.NET Core Identity

PAM owns its identity stack. There is no external IDP process.

- **OpenIddict** (MIT, embedded as a NuGet package) issues OAuth 2.0 / OIDC
  tokens. Authorization Code + PKCE for the back-office SPA, refresh tokens
  with rotation, client_credentials pre-enabled for future module-to-module
  calls.
- **ASP.NET Core Identity** owns user storage, password hashing, lockout,
  email confirmation, MFA. Lives in the `Pam.Identity` module.
- **Single host, single Postgres database.** Identity tables and OpenIddict
  tables cohabit `IdentityDbContext` in schema `identity`.
- **Roles + Permissions.** Identity provides `IdentityRole<Guid>` for coarse
  buckets (Owner, Manager, Operator, Accountant). On top of that, a
  `Permission` + `RolePermission` join carries fine-grained codes
  (`operators.read`, `players.write`, …). Both project as claims at
  token-issuance time; both can be referenced from `[Authorize(Policy = …)]`.

This is the "we own the entire code" answer. The tradeoff vs an external IDP
(ZITADEL/Keycloak) is more code we maintain in exchange for: no separate
process, no separate DB, no Brand↔Org sync layer, and any breaking change
hits us only on a deliberate package upgrade. See ADR #16.

`Pam.Identity` lands in Phase 3. Until then, every endpoint is anonymous —
intentional.

## Brand is a first-class aggregate in `Pam.Operators`

Multi-brand is a first-class concern: betanything.eu plus the planned LATAM
and Asia expansions all share this PAM.

- `Brand` (in `Pam.Operators`) owns name, slug (URL-safe identifier, unique
  per platform), jurisdiction (regulatory region), status (Active /
  Suspended / Disabled), and the audit columns. More fields land as the
  module grows (default currency, default language, brand metadata,
  jurisdictional policy refs).
- Every brand-scoped aggregate (Player, Wallet, Bonus, …) carries a
  `BrandId` foreign reference.
- Brand uniqueness is enforced at the DB (`UNIQUE (slug)`) and via an
  in-handler precheck so we can return a typed `AlreadyExistsException` (→
  409) instead of a Postgres unique-violation generic 500.
- Brand resolution at runtime: anonymous registration takes brand context
  from a request header (final shape TBD when Players returns); back-office
  identities carry `brand_id` as a JWT claim issued by the embedded IDP.

When more brand-scoped modules ship, each `DbContext` will gain an EF Core
global query filter on `BrandId` to prevent the "forgot to filter by tenant"
data-leak class of bug.

## Lean integration events, no PII

Integration events carry IDs and routing data only — never email, name, DOB,
or anything else regulators would consider PII. Consumers that need richer
data go through the relevant `Pam.<Module>.Contracts` query (e.g.
`GetBrandByIdQuery`) to fetch it.

The reason is that integration events live in the message broker, the outbox
table, audit logs, and any consumer that listens. Putting PII into them
fans out PII to all of those stores and makes GDPR right-to-be-forgotten a
nightmare. Lean events are the regulated-finance default.

## Error model: ProblemDetails + stable string codes

All errors are RFC 7807 `ProblemDetails`. Domain exceptions carry a stable
string error code (`operators.brand.slug-taken`,
`operators.brand.not-found`, …) under `extensions.code`. Clients program
against the code; messages can be localized and tweaked freely.

Validation errors flow through FluentValidation and become
`ValidationProblemDetails` with a per-field error array.

Every response includes a `traceId` (W3C Activity ID) for support correlation.

## Time

`DateTimeOffset` everywhere, stored as `timestamptz`. `IClock` interface (with
`SystemClock` impl) is injected wherever a timestamp matters, so handlers and
domain code are testable.

`DateTime.Now`, `DateTime.UtcNow`, and `DateTimeOffset.Now` are banned via
`BannedSymbols.txt`. `DateTimeOffset.UtcNow` is allowed (intentional — used
by the `IntegrationEvent` base record's init defaults and similar places
where DI is impractical).

## Audit columns

`Entity<TId>` carries `CreatedAt`, `CreatedByType`, `CreatedById`,
`LastModifiedAt`, `LastModifiedByType`, `LastModifiedById`. The
`AuditableSaveChangesInterceptor` stamps these on every save using `IClock`
and `IUserContext`.

`IUserContext.Current` returns a typed `Actor(ActorType, Id)` — never null,
defaulting to `Actor.System` for background work and `Actor.Anonymous` for
unauthenticated requests. `ActorType ∈ {System, Player, Operator, Service,
Anonymous}`. The Phase 3 `Pam.Identity` `CurrentUserService` will populate
this from JWT claims (user id + a discriminator derived from the issuing
audience or scope set).

This is the regulator-checkable discriminator: a `Player` self-suspending vs
an `Operator` suspending them are distinguishable in the audit columns
without parsing.

This is application audit logging, not regulatory audit logging. Regulatory
audit (immutable, cryptographically chained) is a separate `Pam.Audits`
module that subscribes to integration events. It will arrive when there's a
real regulatory requirement to satisfy.

## Domain events vs integration events

Two distinct concerns:

| Domain event | Integration event |
|---|---|
| Inside one module | Across modules |
| `Pam.<Module>/<Concept>/Events/` | `Pam.<Module>.Contracts/<Concept>/IntegrationEvents/` |
| Internal — refactor freely | Public contract — versioned, breaking changes are real |
| Dispatched in-process by `DispatchDomainEventsInterceptor` (post-save) | Outbox + MassTransit (when consumers arrive) |
| Naming: `BrandCreated` | Naming: `BrandCreatedIntegrationEvent` |

Domain events do not implement MediatR's `INotification`. They implement our
own `IDomainEvent`. A wrapper (`DomainEventNotification<TEvent>`) adapts them
for MediatR dispatch. This keeps the domain free of framework types.

A handler inside the module bridges the two: e.g.
`BrandCreatedDomainHandler` listens to `BrandCreatedDomainEvent` (in
`Pam.Operators`) and publishes `BrandCreatedIntegrationEvent` (from
`Pam.Operators.Contracts`) via MassTransit's `IPublishEndpoint`. The
integration event preserves the original `EventId` and `OccurredAt`.

### Integration events describe what happened, not what to do

Single most important rule for shaping an integration event. The publisher
broadcasts a fact (`UserLoggedIn`, `PlayerRegistered`, `BetSettled`).
Subscribers decide what to do with it.

The trap is publishing a "command-shaped" event:

```csharp
// WRONG — this is RPC dressed up as an event
public sealed record SendEmailIntegrationEvent(
    string To, string Subject, string Body
) : IntegrationEvent;
```

That couples the publisher to:
- the recipient's email + locale (whose data?)
- the email body wording (whose template?)
- whether to send at all (whose policy?)
- per-brand branding (where does it live?)

…and it leaks PII through every queue and audit log it touches.

```csharp
// RIGHT — describe the fact, leave the action to subscribers
public sealed record UserLoggedInIntegrationEvent(
    Guid UserId,
    string IpAddress,
    string UserAgent,
    string? DeviceFingerprint
) : IntegrationEvent;
```

`Pam.Notifications` (when it ships) subscribes, queries Identity for the
user's contact + locale, decides if the login warrants an email, renders
the template, and only *then* enqueues a send-email job. The publisher
stays ignorant of all that.

### Integration events vs job queues — both ride RabbitMQ

Two different patterns, both transported by MassTransit/RabbitMQ.

|  | Integration event | Job queue |
|---|---|---|
| Question | "What happened?" | "When can this work run?" |
| Producer | Notifies the world | Schedules work |
| Consumers | Many — fan-out broadcast | One worker per job |
| Caller waits? | No (fire-and-forget) | Sometimes (poll for status) |
| Retry/scheduling? | Optional | Core feature |
| Coupling | Decouples *modules* from each other | Decouples *time of request* from *time of work* |

A typical flow chains them:

```
1. POST /v1/identity/login                 (synchronous, ~50ms)
   ├── validate, sign in, set cookie
   ├── publish UserLoggedInIntegrationEvent
   └── return 204
                                           (RabbitMQ exchange)
                                                  ↓
2. Pam.Notifications.UserLoggedInConsumer  (cross-module subscriber)
   ├── query Identity for user contact + locale
   ├── apply policy ("first IP this week? new device?")
   ├── render template
   └── enqueue SendEmail job
                                           (RabbitMQ queue)
                                                  ↓
3. EmailSender worker                      (internal job consumer)
   ├── call SES / SendGrid / SMTP
   ├── retry on failure with exponential backoff
   └── dead-letter after N failures
```

Step 1 is synchronous — the user is waiting. Step 2 is the integration
event (decouple modules). Step 3 is the job queue (decouple work from
time, with retries). Three concerns, one broker.

### What gets queued vs what stays synchronous

Rule of thumb: anything where the user is waiting for the result of a
*decision* runs synchronously. Anything that's a side effect of that
decision runs async.

| Operation | Pattern | Why |
|---|---|---|
| `POST /login` | Synchronous | User waiting; cookie in response |
| `POST /register` | Synchronous | Validation must reject immediately |
| `POST /reset-password` | Synchronous | Validation + token; returns 200 |
| Sending the welcome email | Queued | SMTP can fail; needs retries |
| Sending an SMS via Twilio | Queued | Rate-limited downstream API |
| Webhook to a game provider | Queued | Retry until 200 or dead-letter |
| Bulk-importing players from `gbs-db` | Queued | Long-running |
| Generating a daily revenue report | Queued | Heavy aggregation |
| Real-time bet placement | Synchronous | <50ms latency budget |

Returning 202 Accepted with a job id is occasionally the right call —
mostly for genuinely long-running operations the user explicitly
initiated (a CSV import). It's almost never the right call for a user
action with a yes/no decision behind it.

## Aggregate sizing rules

Three rules, repeat in code review:

1. One command modifies one aggregate in one transaction. If a command needs
   two, the boundary is wrong or you need a saga.
2. Reference other aggregates by ID, never by navigation property.
3. Default to small aggregates. Merge only when an invariant requires atomic
   writes across them.

## Secrets

Two-layer model:

1. **Non-secret config** (URLs, log levels, feature flags) lives in
   `appsettings.{env}.json`, committed.
2. **Secrets** (DB connection strings, RabbitMQ passwords, OpenIddict
   signing/encryption keys, payment-provider HMACs) arrive as environment
   variables from the host. ASP.NET's default configuration precedence
   reads env vars over `appsettings.{env}.json` and treats `__` as the
   nesting separator (`ConnectionStrings__Pam` →
   `ConnectionStrings:Pam`).

In production those env vars are injected by whatever orchestrator runs the
API — systemd unit, k3s Secret, Swarm secret. The orchestrator-managed
secret model is sufficient for the current deployment shape; a dedicated
secret store (HashiCorp Vault, SOPS, k3s External Secrets, ...) is an open
question pinned in `ROADMAP.md` and will be evaluated when the first real
production deploy lands.

## Outbox is not wired yet

Domain events dispatch post-save: the interceptor overrides
`SavedChangesAsync` so handlers see the committed state. The dispatch loop
is bounded at 8 generations to catch handlers that recursively raise events
on tracked aggregates.

Trade-off: post-save dispatch is no longer atomic with the DB write — a
crash between the commit and the handler's side effect leaves the row
without the side effect. For in-process handlers that's acceptable
(restart picks up via change-data scans where needed); for cross-module
broker publishing it isn't, which is exactly what the outbox solves.

When the first cross-module consumer ships, add MassTransit's EF Core
outbox to the publishing module's DbContext so integration events persist
transactionally with the aggregate write.

For Wallet/transactional flows later, the outbox is non-negotiable from day
one of that module.
