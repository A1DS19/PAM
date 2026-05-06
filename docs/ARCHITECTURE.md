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

**Hard rule**: a module never reads another module's data or types. The only
way modules communicate is:

1. **Integration events** through MassTransit + RabbitMQ (default).
2. **In-process queries** through interfaces in `Pam.<Module>.Contracts`.
3. Never via `DbContext` or direct project reference.

The contracts assembly is the seam. A future microservice extraction is
"replace the in-process implementation behind `IPlayerLookup` with a gRPC
client" — same interface, different transport.

## Plural module assemblies

Module assemblies are plural (`Pam.Players`, future `Pam.Wallets`,
`Pam.Bonuses`). The aggregate type is singular (`Player`, `Wallet`, `Bonus`).

The reason is mechanical: a `Pam.Player` namespace + a `Player` class causes
the parent `Pam` namespace's `Player` sub-namespace to shadow the type during
name resolution. Naming the assembly `Pam.Players` removes the collision. The
inner bounded-context folder (`Players/`) stays for organization.

## ZITADEL owns credentials; PAM owns the canonical player

ZITADEL is the credential, session, and tenant-boundary store. The Player
aggregate in `Pam.Players` is the regulatory and business-truth record.

- Player has an `IdentityProviderId` referencing the ZITADEL user id. That is
  the only foreign key to the IDP.
- Player owns: `BrandId`, status (`Pending`/`Active`/`Suspended`/`Closed`),
  jurisdiction, KYC state (later), wallet IDs (later), audit columns. Nothing
  regulatory or business-critical lives in ZITADEL.
- ZITADEL owns: password hash, MFA factors, session list, brute-force
  counters, refresh tokens, password reset / email verification flows.

We deliberately do not use ZITADEL as a user database. The
`IIdentityProvider` interface in `Pam.Players` is the swap seam: a future
move to a different IDP is "implement the same interface against a different
backend."

**Lifecycle changes don't auto-cascade from ZITADEL → PAM.** Deleting a
user in the ZITADEL console does *not* close the PAM player; PAM owns the
canonical record and regulatory retention rules require keeping closed
accounts for years. The intended prod model is **wrap-only**: admins act
through PAM's back-office UI which calls ZITADEL under the covers, so
there's a single source of truth and no sync problem to solve. A
ZITADEL-Actions webhook (option B in `ROADMAP.md`) is the planned
belt-and-suspenders for drift; reconciliation jobs are the safety net
behind that.

The bidirectional link is set up at registration: the Player aggregate gets
`IdentityProviderId = <ZITADEL user id>`. The IDP `sub` claim on incoming
JWTs is the same value, so PAM's runtime mapping is a single-column lookup
on the `players` table. The `player_id` is **not** stuffed into the JWT — we
keep tokens lean and resolve PAM ids server-side.

## Brand = ZITADEL Org

Multi-brand is a first-class concern: betanything.eu plus the planned LATAM
and Asia expansions all share this PAM. Each brand maps to a ZITADEL Org.

- A `Player` belongs to exactly one `BrandId`. The same human registering on
  two brands is two separate `Player` rows — a per-brand record, never a
  cross-brand "Subject" object. (If a regulator later demands cross-brand
  linking — UK GamStop-style central self-exclusion — that's a separate
  optional `Subject` concept layered on top.)
- ZITADEL's Org per Brand maps cleanly: a registration call sets the
  `x-zitadel-orgid` header to the brand's Org id and the user lands inside
  the right tenant boundary.
- Email uniqueness is per-brand (composite unique index on
  `(brand_id, email)`).

Brand context for an incoming registration comes from the `X-Brand` header
(default `betanything-eu`); a runtime `IBrandRegistry` resolves brand id →
ZITADEL Org id. Once authenticated, the brand for a request can be derived
from the player record itself — JWT brand-stuffing is intentionally avoided.

## Operator audience, when it lands

Back-office (operator) auth is deferred. When it arrives, the natural
ZITADEL shape is a separate Project + audience under a dedicated Org for
operators (or a service-account project, depending on the back-office
team's auth shape). `Pam.Api` adds a second `JwtBearer` scheme
(`"operators"`) at that point, and authorization policies pick the scheme
per endpoint group.

## Lean integration events, no PII

Integration events carry IDs and routing data only — never email, name, DOB,
or anything else regulators would consider PII. Consumers that need richer
data go through `IPlayerLookup` (in `Pam.Players.Contracts`) to fetch it.

The reason is that integration events live in the message broker, the outbox
table, audit logs, and any consumer that listens. Putting PII into them
fans out PII to all of those stores and makes GDPR right-to-be-forgotten a
nightmare. Lean events are the regulated-finance default.

## Error model: ProblemDetails + stable string codes

All errors are RFC 7807 `ProblemDetails`. Domain exceptions carry a stable
string error code (`pam.player.email_already_registered`,
`pam.player.age_below_minimum`, etc.) under `extensions.code`. Clients
program against the code; messages can be localized and tweaked freely.

Validation errors flow through FluentValidation and become
`ValidationProblemDetails` with a per-field error array.

Every response includes a `traceId` (W3C Activity ID) for support correlation.

## Time

`DateTimeOffset` everywhere, stored as `timestamptz`. `IClock` interface (with
`SystemClock` impl) is injected wherever a timestamp matters, so handlers and
domain code are testable.

`DateTime.Now`, `DateTime.UtcNow`, and `DateTimeOffset.Now` are banned via
`BannedSymbols.txt`. `DateTimeOffset.UtcNow` is allowed inside `IClock`
implementations and infrastructure code where DI is impractical.

## Audit columns

`Entity<TId>` carries `CreatedAt`, `CreatedByType`, `CreatedById`,
`LastModifiedAt`, `LastModifiedByType`, `LastModifiedById`. The
`AuditableSaveChangesInterceptor` stamps these on every save using `IClock`
and `IUserContext`.

`IUserContext.Current` returns a typed `Actor(ActorType, Id)` — never null,
defaulting to `Actor.System` for background work and `Actor.Anonymous` for
unauthenticated requests. `HttpUserContext` maps the JWT `sub` claim to `ActorType.Player` for any
request authenticated through the players audience. An additional
`"operators"` audience is added later for back-office traffic; at that
point `HttpUserContext` selects `ActorType.Operator` based on the audience
the token was validated against. This is the regulator-checkable
discriminator: a `Player` self-suspending vs an `Operator` suspending them
are distinguishable in the audit columns without parsing.

This is application audit logging, not regulatory audit logging. Regulatory
audit (immutable, cryptographically chained) is a separate `Audit` module
that subscribes to integration events. It will arrive when there's a real
regulatory requirement to satisfy.

## Domain events vs integration events

Two distinct concerns:

| Domain event | Integration event |
|---|---|
| Inside one module | Across modules |
| `Pam.Players/Players/Events/` | `Pam.Players.Contracts/Players/Events/` |
| Internal — refactor freely | Public contract — versioned, breaking changes are real |
| Dispatched in-process by `DispatchDomainEventsInterceptor` (post-save) | Outbox + MassTransit (when consumers arrive) |
| Naming: `PlayerRegistered` | Naming: `PlayerRegisteredIntegrationEvent` |

Domain events do not implement MediatR's `INotification`. They implement our
own `IDomainEvent`. A wrapper (`DomainEventNotification<TEvent>`) adapts them
for MediatR dispatch. This keeps the domain free of framework types.

A handler inside the module bridges the two: `PlayerRegisteredDomainHandler`
listens to the domain event and (eventually) publishes the corresponding
integration event. Right now it logs only — flip it when the first consumer
ships.

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
2. **Secrets** (DB connection strings, ZITADEL admin PAT, RabbitMQ
   passwords, JWT signing keys, payment-provider HMACs) arrive as
   environment variables from the host. ASP.NET's default configuration
   precedence reads env vars over `appsettings.{env}.json` and treats `__`
   as the nesting separator (`Zitadel__AdminPat` → `Zitadel:AdminPat`).

In production those env vars are injected by whatever orchestrator runs
the API — systemd unit, k3s Secret, Swarm secret. The orchestrator-managed
secret model is sufficient for the current deployment shape; a dedicated
secret store (HashiCorp Vault, SOPS, k3s External Secrets, ...) is an
open question pinned in `ROADMAP.md` and will be evaluated when the first
real production deploy lands.

There is no in-process secret-fetching code. An earlier prototype wired
Infisical via a custom `IConfigurationSource`; it was removed because the
bootstrap (admin / org / project / machine identity) was UI-only and never
automated, and the dev encryption keys lived hardcoded in compose. The
plain env-var path matches what every reasonable orchestrator already does
and avoids carrying an unowned dependency.

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

Integration event publishing is currently a no-op (the domain handler
logs). When the first cross-module consumer ships, add MassTransit's
EF Core outbox to `PlayersDbContext` so integration events persist
transactionally with the aggregate write.

For Wallet/transactional flows later, the outbox is non-negotiable from day
one of that module.
