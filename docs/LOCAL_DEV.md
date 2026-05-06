# Local development

## Prerequisites

- .NET 10 SDK (10.0.106 or later patch — pinned in `global.json` with
  `latestFeature` rollForward)
- Docker Desktop / Podman with Compose
- `make`
- `python3` (used by `infra/zitadel/bootstrap.sh`)
- `curl`

## First-time setup (mprocs — recommended)

```bash
mprocs                     # one command; three panes
```

`mprocs.yaml` runs three procs:

- `services` — `docker compose up` (Postgres, ZITADEL, RabbitMQ, Redis, Seq) in foreground
- `bootstrap` — sleeps 30s for ZITADEL, then `make zitadel-bootstrap`. Creates the two Orgs (`betanything-eu`, `betanything-latam-stub`) + the `pam-player-api` Project. Writes `.env.zitadel`. Exits when done.
- `api` — sleeps 60s, then `make dev-api` which applies EF migrations, sources `.env.zitadel`, and runs `dotnet watch`.

Switch panes with `Tab`; quit with `q` or `Ctrl-C`.

## First-time setup (manual)

```bash
make up                              # docker compose up -d + ZITADEL bootstrap
make migrate-update MODULE=Players   # apply EF migrations
make dev-api                         # source .env.zitadel + dotnet watch
make test                            # 22 unit tests, ~40ms (separate terminal)
```

API at `http://localhost:5000`. Scalar UI at `/scalar/v1`. OpenAPI spec at
`/openapi/v1.json`. ZITADEL console at `http://localhost:8080` (initial
admin: `zitadel-admin@zitadel.localhost` / `Password1!`).

## Smoke test

```bash
# Default brand (betanything-eu) — no header needed.
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

# Explicit brand via X-Brand header (provisioned LATAM stub Org).
curl -i -X POST http://localhost:5000/v1/auth/register \
  -H "Content-Type: application/json" \
  -H "X-Brand: betanything-latam-stub" \
  -d '{ ... }'
```

## Service ports

| Service | Host port | Notes |
|---|---|---|
| Postgres (PAM) | 5432 | user: `pam`, db: `pam` |
| Postgres (ZITADEL) | 5433 | user: `postgres`, db: `zitadel` |
| ZITADEL | 8080 | console + OIDC; insecure dev mode |
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

## Re-running ZITADEL bootstrap

Idempotent — finds existing Orgs/Project by name and reuses them:

```bash
make zitadel-bootstrap
```

If you ever wipe the ZITADEL volume (`docker compose down -v`), the bootstrap
re-runs from scratch on next `make up`.

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
| ZITADEL bootstrap (orgs + project + PAT extraction) | `infra/zitadel/bootstrap.sh` |

## Secrets

Local dev: `appsettings.json` carries non-secret defaults. The ZITADEL
admin PAT and Org IDs come from `.env.zitadel` (generated by the bootstrap
script). The API needs them at startup — either paste them into a
git-ignored `appsettings.Development.json`, or export them as env vars:

```bash
set -a
source .env.zitadel
export Zitadel__AdminPat="$ZITADEL_ADMIN_PAT"
export Brands__Map__betanything-eu__ZitadelOrgId="$ZITADEL_ORG_BETANYTHING_EU"
export Brands__Map__betanything-latam-stub__ZitadelOrgId="$ZITADEL_ORG_BETANYTHING_LATAM_STUB"
set +a
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
