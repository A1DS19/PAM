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
| Postgres (Infisical) | 5434 | user: `infisical`, db: `infisical` |
| Keycloak | 8080 | admin: `admin/admin`; realm import on startup |
| RabbitMQ | 5672 (amqp), 15672 (UI) | user: `pam` |
| Redis (PAM) | 6379 | password: `redis_dev_password` (currently disabled in compose due to port conflict) |
| Redis (Infisical) | 6381 | internal to Infisical |
| Seq | 5341 (ingest), 8090 (UI) | currently disabled in compose due to port conflict |
| Infisical | 8085 (UI/API) | first-time setup via UI; see Secrets below |
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

## Secrets (Infisical)

Infisical runs as a service in `docker-compose.yml`. Local dev does **not**
require it — `Pam.Api` falls back to `appsettings.{env}.json` when the
Infisical env vars aren't set. Use it when you want prod-like behavior or
when you're testing the secret-loading path.

### One-time UI bootstrap

Infisical's first-run sequence happens through the web UI; it can't be
fully scripted without using internal APIs.

1. Open `http://localhost:8085`.
2. Create the first admin account.
3. Create an organization (e.g., `pam`).
4. Create a project (e.g., `pam-dev`). Note the **Project ID** — paste it
   into `appsettings.{env}.json` under `Infisical:ProjectId`.
5. Add the environments you want (`dev`, `staging`, `production` — `dev`
   exists by default).
6. Add secrets in the `dev` environment. Use `__` as the nesting separator
   so it maps to ASP.NET configuration:

   ```
   ConnectionStrings__Pam      → ConnectionStrings:Pam
   ConnectionStrings__Redis    → ConnectionStrings:Redis
   Keycloak__AdminPassword     → Keycloak:AdminPassword
   MessageBroker__Password     → MessageBroker:Password
   ```

7. Create a **Machine Identity** (Access Control → Identities). Attach the
   `Universal Auth` method, generate a Client ID + Client Secret, and grant
   the identity Read access to the project's `dev` environment.

### Run with Infisical

Export the Machine Identity credentials before `make run`:

```bash
export INFISICAL_CLIENT_ID=<from UI>
export INFISICAL_CLIENT_SECRET=<from UI>
make run
```

The configuration provider runs at startup, fetches all secrets in the
configured environment+path, maps `__` → `:`, and the values override
`appsettings.json`. If the call fails or env vars are missing, it's a
no-op (Optional=true) and `appsettings.json` values stand.

To force the API to fail fast on missing Infisical config, set
`Infisical:Optional` to `false` in the relevant `appsettings.{env}.json`.

## Scalar / OpenAPI

Scalar UI is dev-only (`if (app.Environment.IsDevelopment())`). To enable in
staging or behind admin auth, pull `MapOpenApi()` and `MapScalarApiReference()`
out of the dev-only block in `Program.cs` and add the appropriate
`RequireAuthorization` chain.
