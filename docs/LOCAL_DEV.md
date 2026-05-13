# Local development

## Prerequisites

- .NET 10 SDK (10.0.106 or later patch — pinned in `global.json` with
  `latestFeature` rollForward)
- Docker (or Podman) with Compose
- `make`

## First-time setup (mprocs — recommended)

```bash
mprocs                     # one command; two panes
```

`mprocs.yaml` runs two procs:

- `services` — `docker compose up` (SQL Server, RabbitMQ, Redis, Seq, Grafana LGTM) in foreground
- `api` — `make dev-api` (apply migrations + `dotnet watch`)

Switch panes with `Tab`; quit with `q` or `Ctrl-C`.

## First-time setup (manual)

```bash
make up                                  # docker compose up -d
make migrate-update MODULE=Operators     # apply EF migrations
make dev-api                             # dotnet watch
make test                                # unit tests in a separate terminal
```

| URL | What |
|---|---|
| `http://localhost:5000` | PAM API |
| `http://localhost:5000/scalar/v1` | Scalar OpenAPI UI |
| `http://localhost:8090` | Seq logs (no auth in dev) |
| `http://localhost:3001` | Grafana — traces (Tempo), metrics (Mimir), logs (Loki) |

## Smoke test

```bash
# Create a brand.
curl -i -X POST http://localhost:5000/v1/operators/brands \
  -H "Content-Type: application/json" \
  -d '{"name":"BetAnything EU","slug":"betanything-eu","jurisdiction":"EU"}'
# → 201 Created
#   Location: /v1/operators/brands/<brand-id>
#   { "id": "<brand-id>" }

# Read it back.
curl -s http://localhost:5000/v1/operators/brands/<brand-id> | jq
```

The API is anonymous in Phase 2. Authentication arrives in Phase 3 with
the `Pam.Identity` module (OpenIddict + ASP.NET Identity); endpoints will
then default to `RequireAuthorization` and individual ones opt out via
`.AllowAnonymous()`.

## Service ports

| Service | Host port | Notes |
|---|---|---|
| SQL Server | 1433 | user: `sa`, password: `Pam_dev_password_123!`, db: `pam`. On Apple Silicon the image runs under Rosetta (`platform: linux/amd64`) — first boot is ~30–60s while it lays down system DBs. |
| RabbitMQ | 5672 (amqp), 15672 (UI) | user: `pam` |
| Redis | 6379 | password: `redis_dev_password` |
| Seq | 5341 (ingest), 8090 (UI) | optional log viewer |
| otel-lgtm | 4317 (OTLP gRPC), 4318 (OTLP HTTP), 3001 (Grafana UI) | traces+metrics+logs (Tempo/Mimir/Loki) |
| Pam.Api | 5000 | |

### Known port conflicts

If you have a peer project (`dmt-redis`, `dmt-seq`) running with the same
defaults, our `redis` and `seq` services will fail to start. Either:

- Stop the conflicting containers (`docker stop dmt-redis dmt-seq`), or
- Skip those services for now — neither is required to run the API. Logs go
  to console; Redis only matters once distributed rate limiting goes in.

## EF migrations

The Makefile is parameterized by `MODULE`:

```bash
make migrate-add MODULE=Operators NAME=AddSomething
make migrate-update MODULE=Operators
make migrate-status MODULE=Operators
make migrate-remove MODULE=Operators      # removes the last unapplied migration
```

A `IDesignTimeDbContextFactory<OperatorsDbContext>` lives in
`src/Modules/Operators/Pam.Operators/Data/`, so EF tooling can spin up the
context standalone. The connection string can be overridden via
`PAM_DESIGN_CONNECTION` env var (defaults to the local compose).

`Microsoft.EntityFrameworkCore.Design` is a `PackageReference` on
`Pam.Api` (the startup project). EF Tools require it there even though
the design-time factory is in the module project — the error message
("Your startup project doesn't reference …") is misleading.

## Adding a feature

The `CreateBrand` feature in
`src/Modules/Operators/Pam.Operators/Brands/Features/CreateBrand/` is the
canonical template. Four files per feature:

```
<FeatureName>/
├── <FeatureName>Command.cs     # ICommand<TResponse> or IQuery<TResponse>
├── <FeatureName>Validator.cs   # AbstractValidator<TCommand>
├── <FeatureName>Handler.cs     # ICommandHandler<,> or IQueryHandler<,>
└── <FeatureName>Endpoint.cs    # ICarterModule with one MapPost/Get/...
```

MediatR + FluentValidation auto-discover via assembly scanning. Carter
discovers endpoints via the `DependencyContextAssemblyCatalog` registered
in `Program.cs`. No manual DI registration needed.

Domain events live alongside the aggregate
(`<Concept>/Events/<Event>.cs`); a sibling
`<Concept>/EventHandlers/<Event>DomainHandler.cs` listens for the domain
event and publishes the corresponding integration event from
`Pam.<Module>.Contracts`.

## Adding a module

Mirror `Pam.Operators`. Three projects per module, plus the test project:

```
src/Modules/<Module>/
  Pam.<Module>/                 # csproj + <Module>Module.cs at root
  Pam.<Module>.Contracts/       # DTOs, IQuery<T>, integration events
tests/Pam.<Module>.UnitTests/
```

`<Module>Module.cs` exposes two extension methods used by `Pam.Api`:

- `AddXModule(this IServiceCollection, IConfiguration)` — registers
  DbContext, interceptors, health checks.
- `UseXModuleAsync(this IServiceProvider)` — runs `MigrateAsync` on
  startup.

Interceptors (`AuditableSaveChangesInterceptor`,
`DispatchDomainEventsInterceptor` from `Pam.Shared`) are registered with
`services.TryAddScoped<...>()` so multiple modules don't conflict.

Persistence uses schema-per-module (`modelBuilder.HasDefaultSchema("…")`
plus `sql.MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")`
inside the `UseSqlServer(...)` callback so each module's history table
lives next to its tables instead of in `dbo`). Snake_case columns via
`options.UseSnakeCaseNamingConvention()` from the
`EFCore.NamingConventions` package — apply on **both** runtime and
design-time DbContext setup. Health probe via
`AddHealthChecks().AddSqlServer(connectionString, name: "<module>-db",
tags: ["ready", "module:<module>"])`.

After scaffolding the module, register it in `Pam.Api`:

```csharp
var moduleAssemblies = new[] { typeof(OperatorsModule).Assembly /*, …*/ };

builder.Services.AddPamMediatR(moduleAssemblies);
builder.Services.AddCarter(new DependencyContextAssemblyCatalog(moduleAssemblies));
builder.Services.AddOperatorsModule(builder.Configuration);
// later: await app.Services.UseOperatorsModuleAsync();
```

## Where things live

| Concern | Location |
|---|---|
| Cross-cutting infra (DDD primitives, behaviors, exceptions, interceptors) | `src/Shared/Pam.Shared/` |
| CQRS interfaces, IDomainEvent, Actor, PamIds | `src/Shared/Pam.Shared.Contracts/` |
| MassTransit registration, IntegrationEvent base | `src/Shared/Pam.Shared.Messaging/` |
| Per-module aggregates, features, EF, wire-up | `src/Modules/<Module>/Pam.<Module>/` |
| Public DTOs, queries, integration events | `src/Modules/<Module>/Pam.<Module>.Contracts/` |
| API host, DI wiring, OTel, health, OpenAPI | `src/Bootstrapper/Pam.Api/` |

## Secrets

Local dev: `appsettings.json` carries non-secret defaults.

Production: secrets arrive as env vars from whatever orchestrator runs
the API (systemd unit, k3s Secret, Swarm secret). ASP.NET's default
configuration precedence reads env vars over `appsettings.{env}.json`
and treats `__` as the nesting separator. A dedicated secret store
(HashiCorp Vault, SOPS, k3s External Secrets, etc.) is open — see
[`ROADMAP.md`](ROADMAP.md).

## Observability (OpenTelemetry)

Pam.Api emits traces, metrics and logs over OTLP to the local Grafana
LGTM container (`otel-lgtm` in compose). Open Grafana at
`http://localhost:3001` — Tempo (traces), Mimir (metrics) and Loki (logs)
are pre-wired as datasources. Resource attributes (`service.name`,
`service.version`, `deployment.environment`, `host.name`) tag every
signal so you can segment by env/instance once more than one runs.

If LGTM isn't running, the SDK retries OTLP exports in the background and
logs warnings — the API itself keeps serving. To point at a different
collector (Grafana Cloud, a real Tempo, Honeycomb, Datadog, …) set
`OTEL_EXPORTER_OTLP_ENDPOINT` (and for the Serilog log sink,
`Serilog:WriteTo:2:Args:Endpoint` via env var) at the orchestrator.

## Scalar / OpenAPI

Scalar UI is dev-only (`if (app.Environment.IsDevelopment())`). To enable
in staging or behind admin auth, pull `MapOpenApi()` and
`MapScalarApiReference()` out of the dev-only block in `Program.cs` and
add the appropriate `RequireAuthorization` chain.

## Common gotchas

- `dotnet run --no-build` runs the cached DLL — newly generated
  migrations won't be picked up. Always `dotnet build` after
  `make migrate-add` before re-running the API.
- `BannedSymbols.txt` blocks `DateTime.UtcNow`, `DateTime.Now`, and
  `DateTimeOffset.Now`. `DateTimeOffset.UtcNow` is allowed (intentional —
  see the `IntegrationEvent` base record).
- `dotnet watch` watches files reachable from `Pam.Api`. If
  `mprocs.log` (or any other runtime artifact) ends up triggering hot
  reload, exclude it via `<Watch Remove="..."/>` in the relevant csproj
  or a `Directory.Build.props` entry.
