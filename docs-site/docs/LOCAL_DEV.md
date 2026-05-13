# Local development

## Prerequisites

- .NET 10 SDK (10.0.106+; pinned in `global.json`)
- Docker (or Podman) with Compose
- `make`

## One-command boot (recommended)

```bash
mprocs
```

Two panes:

- `services` — `docker compose up` (SQL Server, RabbitMQ, Redis, Seq,
  Grafana LGTM, docs-site)
- `api` — `make -C api dev-api` (apply migrations + `dotnet watch`)

`Tab` to switch panes; `q` or `Ctrl-C` to quit.

## Manual

```bash
make -C api up                                  # docker compose up -d
make -C api migrate-update MODULE=Operators     # EF migrations
make -C api dev-api                             # dotnet watch
make -C api test                                # in another terminal
```

## URLs

| URL | What |
|---|---|
| `http://localhost:5000` | PAM API |
| `http://localhost:5000/scalar/v1` | Scalar OpenAPI UI |
| `http://localhost:4000` | Docusaurus docs site |
| `http://localhost:8090` | Seq (no auth in dev) |
| `http://localhost:3001` | Grafana — Tempo / Mimir / Loki |
| `http://localhost:8025` | Mailpit web UI |
| `http://localhost:15672` | RabbitMQ UI (`pam` / `rabbit_dev_password`) |

## Smoke test

```bash
curl -i -X POST http://localhost:5000/v1/operators/brands \
  -H "Content-Type: application/json" \
  -d '{"name":"BetAnything EU","slug":"betanything-eu","jurisdiction":"EU"}'
# → 201 Created; Location: /v1/operators/brands/<id>

curl -s http://localhost:5000/v1/operators/brands/<id> | jq
```

API is anonymous in Phase 2. Auth lands in Phase 3 with `Pam.Identity`.

## Service ports

| Service | Host port | Notes |
|---|---|---|
| SQL Server | 1433 | `sa` / `Pam_dev_password_123!`, db `pam`. Apple Silicon runs under Rosetta (`platform: linux/amd64`); first boot 30–60s. |
| RabbitMQ | 5672, 15672 | user `pam` |
| Redis | 6379 | password `redis_dev_password` |
| Seq | 5341 (ingest), 8090 (UI) | optional |
| otel-lgtm | 4317 (gRPC), 4318 (HTTP), 3001 (Grafana) | traces + metrics + logs |
| Mailpit | 1025 (smtp), 8025 (UI) | SMTP dev sink |
| docs-site | 4000 | Docusaurus dev server |
| Pam.Api | 5000 | |

### Port conflicts

If a peer project (`dmt-redis`, `dmt-seq`) is up with same defaults, our
`redis` and `seq` will fail to start. Stop the conflicting containers
or skip those services — neither is required to run the API.

## EF migrations

Makefile is parameterized by `MODULE`:

```bash
make -C api migrate-add MODULE=Operators NAME=AddSomething
make -C api migrate-update MODULE=Operators
make -C api migrate-status MODULE=Operators
make -C api migrate-remove MODULE=Operators       # last unapplied
```

A `IDesignTimeDbContextFactory<…DbContext>` per module lets EF tools
spin the context standalone. Override the connection string with
`PAM_DESIGN_CONNECTION` env var.

`Microsoft.EntityFrameworkCore.Design` is on `Pam.Api` (the startup
project) — required even though the factory lives in the module
project. The "Your startup project doesn't reference …" error message
is misleading.

## Adding a feature

`api/src/Modules/Operators/Pam.Operators/Brands/Features/CreateBrand/`
is the canonical template. Four files:

```
<FeatureName>/
├── <FeatureName>Command.cs     # ICommand<TResponse> or IQuery<TResponse>
├── <FeatureName>Validator.cs   # AbstractValidator<TCommand>
├── <FeatureName>Handler.cs     # ICommandHandler<,> or IQueryHandler<,>
└── <FeatureName>Endpoint.cs    # ICarterModule with one MapPost/Get/...
```

MediatR + FluentValidation auto-discover via assembly scanning. Carter
discovers endpoints via `DependencyContextAssemblyCatalog` in
`Program.cs`. No manual DI registration.

Domain events live alongside the aggregate (`<Concept>/Events/`); a
sibling `EventHandlers/<Event>DomainHandler.cs` bridges to the matching
integration event in `Pam.<Module>.Contracts`.

## Adding a module

Mirror `Pam.Operators`. Three projects + test project:

```
api/src/Modules/<Module>/
  Pam.<Module>/                 # csproj + <Module>Module.cs at root
  Pam.<Module>.Contracts/       # DTOs, IQuery<T>, integration events
api/tests/Pam.<Module>.UnitTests/
```

`<Module>Module.cs` exposes:

- `AddXModule(services, config)` — DbContext, interceptors, health check.
- `UseXModuleAsync(serviceProvider)` — `MigrateAsync` on startup.

Interceptors (`AuditableSaveChangesInterceptor`,
`DispatchDomainEventsInterceptor`) registered with `TryAddScoped` so
modules don't conflict.

Persistence: schema-per-module via `HasDefaultSchema("…")` +
`MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")`. Snake_case
columns via `EFCore.NamingConventions` on **both** runtime and
design-time. Health probe:
`AddSqlServer(conn, name: "<module>-db", tags: ["ready", "module:<module>"])`.

Register in `Pam.Api`:

```csharp
var moduleAssemblies = new[] { typeof(OperatorsModule).Assembly /* , … */ };

builder.Services.AddPamMediatR(moduleAssemblies);
builder.Services.AddCarter(new DependencyContextAssemblyCatalog(moduleAssemblies));
builder.Services.AddOperatorsModule(builder.Configuration);
// later: await app.Services.UseOperatorsModuleAsync();
```

## Where things live

| Concern | Location |
|---|---|
| Cross-cutting infra (DDD primitives, behaviors, interceptors) | `api/src/Shared/Pam.Shared/` |
| CQRS interfaces, `IDomainEvent`, `Actor`, `PamIds` | `api/src/Shared/Pam.Shared.Contracts/` |
| MassTransit registration, `IntegrationEvent` base | `api/src/Shared/Pam.Shared.Messaging/` |
| Module internals | `api/src/Modules/<Module>/Pam.<Module>/` |
| Module public surface | `api/src/Modules/<Module>/Pam.<Module>.Contracts/` |
| API host (DI, OTel, health, OpenAPI) | `api/src/Bootstrapper/Pam.Api/` |

## Secrets

Local: `appsettings.json` has non-secret defaults.

Production: env vars from the orchestrator. ASP.NET reads env vars over
JSON; `__` is the nesting separator. A dedicated secret store
(Vault, SOPS, External Secrets) is open — `ROADMAP.md`.

## Observability (OpenTelemetry)

`Pam.Api` emits traces, metrics, logs over OTLP to the local
`otel-lgtm` container. Grafana at `http://localhost:3001` — Tempo,
Mimir, Loki pre-wired as datasources. Resource attrs
(`service.name`, `service.version`, `deployment.environment`,
`host.name`) tag every signal.

If LGTM isn't running, the SDK retries in the background — the API
keeps serving. Point at a different collector by setting
`OTEL_EXPORTER_OTLP_ENDPOINT`.

## Scalar / OpenAPI

Scalar UI is dev-only (`if (app.Environment.IsDevelopment())`). To
enable in staging or behind admin auth, pull `MapOpenApi()` +
`MapScalarApiReference()` out of the dev-only block and add a
`RequireAuthorization` chain.

## Common gotchas

- `dotnet run --no-build` runs the cached DLL — new migrations won't be
  picked up. Always `dotnet build` after `make migrate-add`.
- `BannedSymbols.txt` blocks `DateTime.UtcNow`, `DateTime.Now`,
  `DateTimeOffset.Now`. `DateTimeOffset.UtcNow` is allowed (used in
  `IntegrationEvent` record init defaults).
- `dotnet watch` watches files reachable from `Pam.Api`. If a runtime
  artifact (e.g. `mprocs.log`) triggers reloads, exclude via
  `<Watch Remove="..."/>` in the relevant csproj or
  `Directory.Build.props`.
