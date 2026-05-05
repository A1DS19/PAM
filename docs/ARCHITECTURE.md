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

## Keycloak owns credentials; PAM owns the canonical player

Keycloak is the credential and session store. The Player aggregate in
`Pam.Players` is the regulatory and business-truth record.

- Player has an `IdentityProviderId` referencing the Keycloak `sub`. That is
  the only foreign key to the IDP.
- Player owns: status (`Pending`/`Active`/`Suspended`/`Closed`), jurisdiction,
  KYC state (later), wallet IDs (later), audit columns. Nothing regulatory or
  business-critical lives in Keycloak attributes.
- Keycloak owns: password hash, MFA factors, session list, brute-force
  counters, refresh tokens, password reset / email verification flows.

We deliberately do not use Keycloak as a user database. The vendor will
change one day.

The bidirectional link is set up at registration: the Player aggregate gets
`IdentityProviderId = <Keycloak sub>`, and the Keycloak user gets a
`player_id` custom attribute. The `player_id` is mapped into access tokens via
a protocol mapper on the `pam.player` client scope, so authenticated requests
arrive carrying both the Keycloak `sub` and the PAM `player_id`.

## Two realms, never one

We will run a `players` realm and (when admin endpoints land) an `operators`
realm. They have different password policies, MFA requirements, session
lifetimes, themes, and federation needs. Trying to do this in one realm with
role flags is a known anti-pattern.

`Pam.Api` validates tokens from both realms via separate `JwtBearer` schemes
(`"players"` and `"operators"`), with authorization policies selecting the
scheme per endpoint group.

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

`Entity<TId>` carries `CreatedAt`, `CreatedBy`, `LastModifiedAt`,
`LastModifiedBy`. The `AuditableSaveChangesInterceptor` stamps these on every
save using `IClock` and `IUserContext`. `IUserContext` reads from JWT claims
(`sub`, falling back to `player_id`).

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
| Dispatched in-process by `DispatchDomainEventsInterceptor` (pre-save) | Outbox + MassTransit (when consumers arrive) |
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

1. **Non-secret config** (URLs, log levels, feature flags, the Infisical
   project ID itself) lives in `appsettings.{env}.json`, committed.
2. **Secrets** (DB connection strings, Keycloak admin credentials, RabbitMQ
   passwords, JWT signing keys, payment-provider HMACs) live in **Infisical**,
   self-hosted in `docker-compose.yml` and run on the on-prem hardware in
   non-dev environments.

The `Pam.Api` host carries an `InfisicalSecretsConfigurationProvider` that
runs at startup, authenticates via Universal Auth (Machine Identity), pulls
secrets from a project + environment, maps `__` → `:` to match ASP.NET
configuration keys, and inserts them as the highest-priority configuration
source. When `INFISICAL_CLIENT_ID` / `INFISICAL_CLIENT_SECRET` env vars are
absent, the provider is a no-op and `appsettings.json` wins (the local-dev
default).

The Machine Identity credentials themselves are the bootstrap secret. In
prod they're injected as env vars by whatever orchestrator runs the API
(systemd unit, k3s Secret, Swarm secret); they are **not** committed and
**not** stored back in Infisical (chicken-and-egg).

## Outbox is not wired yet

Domain events dispatch pre-save (atomic with the DB write through the
interceptor). Integration event publishing is currently a no-op (the domain
handler logs). When the first cross-module consumer ships, add MassTransit's
EF Core outbox to `PlayersDbContext` so integration events persist
transactionally with the aggregate write.

For Wallet/transactional flows later, the outbox is non-negotiable from day
one of that module.
