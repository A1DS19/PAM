# Local development

## Prerequisites

- .NET 10 SDK (10.0.203 or later patch — pinned in `global.json`)
- Docker Desktop
- `make`

## First-time setup

```bash
# 1. Bring up dependencies (Postgres, Keycloak, RabbitMQ, optionally Redis/Seq).
#    Also runs the post-import script that declares the `player_id` attribute
#    on the players-realm user profile (idempotent).
make up

# 2. Apply EF migrations.
make migrate-update MODULE=Players

# 3. Run the API.
make run

# 4. (Optional) Run the unit tests.
make test
```

API at `http://localhost:5000`. Scalar UI at `/scalar/v1`. OpenAPI spec at
`/openapi/v1.json`.

## Smoke test

```bash
curl -i -X POST http://localhost:5000/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@example.com",
    "password": "Aa1!aaaaaaaaaa",
    "firstName": "Alice",
    "lastName": "Tester",
    "dateOfBirth": "1990-04-15",
    "countryCode": "US"
  }'
# → 201 Created, Location: /v1/players/<player-id>
```

## Service ports

| Service | Host port | Notes |
|---|---|---|
| Postgres (PAM) | 5432 | user: `pam`, db: `pam` |
| Postgres (Keycloak) | 5433 | user: `keycloak`, db: `keycloak` |
| Keycloak | 8080 | admin: `admin/admin`; realm import on startup |
| RabbitMQ | 5672 (amqp), 15672 (UI) | user: `pam` |
| Redis | 6379 | password: `redis_dev_password` |
| Seq | 5341 (ingest), 8090 (UI) | optional log viewer |
| Pam.Api | 5000 | |

### Known port conflicts

If you have a peer project (`dmt-redis`, `dmt-seq`) running with the same
defaults, our `redis` and `seq` services will fail to start. Either:

- Stop the conflicting containers (`docker stop dmt-redis dmt-seq`), or
- Skip those services for now — neither is required to run the API. Logs go
  to console; Redis only matters once distributed rate limiting goes in.

## EF migrations

The Makefile is parameterized by `MODULE`. The module directory must be
plural (e.g. `Players`):

```bash
make migrate-add MODULE=Players NAME=AddSomething
make migrate-update MODULE=Players
make migrate-status MODULE=Players
make migrate-remove MODULE=Players      # removes the last unapplied migration
```

A `IDesignTimeDbContextFactory<PlayersDbContext>` lives in
`src/Modules/Players/Pam.Players/Data/`, so EF tooling does not require a
startup project. The connection string can be overridden via the
`PAM_CONNECTION` env var (defaults to the local compose).

## Running multiple things at once

For HTTP + DB + Keycloak:

```bash
make up           # one-time per session
make run          # `dotnet watch` — hot reload on .cs changes
```

To stop everything:

```bash
make down         # stops compose services
# Ctrl-C the `make run` terminal
```

## Adding a feature

The `Register` feature in
`src/Modules/Players/Pam.Players/Players/Features/Register/` is the canonical
template. Five files per feature:

```
<FeatureName>/
├── <FeatureName>.cs           # ICommand<TResponse> or IQuery<TResponse> record
├── <FeatureName>Validator.cs  # AbstractValidator<TCommand>
├── <FeatureName>Handler.cs    # ICommandHandler<,> or IQueryHandler<,>
├── <FeatureName>Endpoint.cs   # ICarterModule with one MapPost/Get/...
└── (optional) DomainEventHandler — if the command raises events
```

MediatR + FluentValidation auto-discover via assembly scanning. Carter
discovers endpoints via the `DependencyContextAssemblyCatalog` registered in
`Program.cs`. No manual DI registration needed for any of the five.

## Where things live

| Concern | Location |
|---|---|
| Cross-cutting infra (DDD primitives, behaviors, exceptions, interceptors) | `src/Shared/Pam.Shared/` |
| CQRS interfaces, IDomainEvent, typed-id helpers | `src/Shared/Pam.Shared.Contracts/` |
| MassTransit registration, IntegrationEvent base | `src/Shared/Pam.Shared.Messaging/` |
| Per-module aggregates, features, infra | `src/Modules/<Module>/Pam.<Module>/` |
| Public integration events / read-model interfaces | `src/Modules/<Module>/Pam.<Module>.Contracts/` |
| API host, DI wiring, auth, healthchecks, OpenAPI | `src/Bootstrapper/Pam.Api/` |
| Keycloak realm import + post-import setup | `infra/keycloak/realms/`, `infra/keycloak/setup/` |

## Secrets

Local dev: `appsettings.json` and `appsettings.Development.json` carry
working values for Keycloak, Postgres, Redis, RabbitMQ. Nothing here is
production-grade — credentials are dev defaults.

Override locally without committing: either `dotnet user-secrets` (per
project) or environment variables (ASP.NET layers env vars over
appsettings using `__` as the nesting separator):

```bash
export ConnectionStrings__Pam="Host=localhost;Database=pam;Username=pam;Password=<your-pw>"
export Keycloak__AdminPassword="<your-pw>"
make run
```

Production: secrets arrive as env vars from whatever orchestrator runs the
API (systemd unit, k3s Secret, Swarm secret). A dedicated secret store
(HashiCorp Vault, SOPS, k3s External Secrets, etc.) is open — see
`ROADMAP.md`.

## Scalar / OpenAPI

Scalar UI is dev-only (`if (app.Environment.IsDevelopment())`). To enable in
staging or behind admin auth, pull `MapOpenApi()` and `MapScalarApiReference()`
out of the dev-only block in `Program.cs` and add the appropriate
`RequireAuthorization` chain.
