# PAM — Player Admin Manager

A regulated iGaming back-office system, replacing the legacy GBS Core
Platform. Built as a .NET 10 modular monolith with explicit seams so
individual modules can be extracted to their own services when scale
or team-org demands it.

## Status

Seven modules live on `main`:

| Module | What it owns |
|---|---|
| `Pam.Identity` | OpenIddict OAuth/OIDC + ASP.NET Core Identity (back-office users, roles, permissions, MFA, password reset) |
| `Pam.Operators` | Brands and brand-scoped configuration |
| `Pam.Players` | Player aggregate (brand-scoped, identity-authenticated) |
| `Pam.Wallet` | Balances and ledger |
| `Pam.Ingest` | Vendor transaction capture — 21G REST + SOAP, FastSpin Phase-A intercept (forwards to GBS, captures both directions) |
| `Pam.Notifications` | SMTP-backed email + integration-event subscribers |
| `Pam.Audit` | Cross-cutting command-log via MediatR pipeline |

Vendor ingestion is the hot path. See `docs-fe/docs/INGEST.md` for the
canonical request flow, idempotency model, and the strangler-fig
migration phases (A → B → C).

## Stack

- **.NET 10** · ASP.NET Core, EF Core 10, **SQL Server 2022**
  (schema-per-module, snake_case columns via `EFCore.NamingConventions`)
- **Identity:** embedded OpenIddict 7 + ASP.NET Core Identity. In-process
  IDP — no separate auth host.
- **Endpoints:** Carter (vertical slices), Scalar OpenAPI in dev
- **Mediator + validation:** MediatR 12.4.1 (pinned MIT) +
  FluentValidation 12
- **Messaging:** MassTransit 8.5.9 (pinned Apache-2.0) + RabbitMQ,
  single bus-wide outbox on `Pam.Shared.Messaging` with reconciler
  backstop
- **Caching:** Redis (StackExchange.Redis) for query caching + the
  partitioned rate limiter (multi-replica safe)
- **Observability:** Serilog → Seq + OpenTelemetry (traces, metrics, EF
  instrumentation) → otel-lgtm (Grafana + Tempo + Loki + Prometheus)
- **Tests:** xUnit v3 + FluentAssertions 7.2.0 (pinned Apache-2.0).
  Architecture tests via NetArchTest. Stress tests via k6.
- **Analyzers:** Meziantou, SonarAnalyzer, BannedApiAnalyzers — all
  warnings-as-errors.

Central package management via `api/Directory.Packages.props`.
License-driven pins (MediatR 12.4.1, MassTransit 8.5.9,
FluentAssertions 7.2.0) are documented in
[`docs-fe/docs/internal/DECISIONS.md`](docs-fe/docs/internal/DECISIONS.md).

## Quick start

```bash
cd api
make up                                # mssql, rabbit, redis, otel-lgtm, seq, mailpit
make dev-api                           # applies Operators migration + dotnet watch
make test                              # full test suite
```

API at `http://localhost:5000`. Scalar UI at `/scalar/v1`. Grafana at
`http://localhost:3001`. Seq at `http://localhost:8090`. Mailpit at
`http://localhost:8025`. First-time setup walkthrough in
[`docs-fe/docs/LOCAL_DEV.md`](docs-fe/docs/LOCAL_DEV.md).

### Stress harness

Separate Makefile at the repo root drives the k6 stress runs against
the hot path (FastSpin Phase-A intercept + 21G REST). Requires
`brew install k6`.

```bash
make stress-up         # bring up the compose stack
make stress-api        # API in Stress mode (rate limiter off, stub upstream)
make stress-reset      # wipe ingest + outbox + audit tables between runs
make stress-fastspin   # k6 against /v1/ingest/vendors/fastspin/main
make stress-21g        # k6 against /v1/ingest/vendors/21g
```

Findings + tunings in [`docs-fe/docs/STRESS.md`](docs-fe/docs/STRESS.md).

## Documentation

Published docs (rendered by the Docusaurus site under `docs-fe/`):

| Doc | Purpose |
|---|---|
| [`ARCHITECTURE.md`](docs-fe/docs/ARCHITECTURE.md) | The **why** — module boundaries, identity model, outbox topology, domain-vs-integration events, aggregate sizing. |
| [`LOCAL_DEV.md`](docs-fe/docs/LOCAL_DEV.md) | First-time setup, service ports, EF migration commands, feature template. |
| [`AUTH.md`](docs-fe/docs/AUTH.md) | OpenIddict + Identity wiring, MFA, password-reset flow, permission model. |
| [`INGEST.md`](docs-fe/docs/INGEST.md) | Vendor ingest path, idempotency, 21G + FastSpin specifics, strangler-fig phases. |
| [`ENDPOINTS.md`](docs-fe/docs/ENDPOINTS.md) | OpenAPI annotation conventions every endpoint must follow. |
| [`EVENTS.md`](docs-fe/docs/EVENTS.md) | Domain events, integration events, outbox flush, reconciler. |
| [`STRESS.md`](docs-fe/docs/STRESS.md) | Stress methodology, findings, tunings, readiness assessment. |
| [`TESTING.md`](docs-fe/docs/TESTING.md) | Unit / integration / architecture / stress conventions. |
| [`CORE_PLATFORM_MAPPING.md`](docs-fe/docs/CORE_PLATFORM_MAPPING.md) | Section-by-section mapping of the legacy Core Platform onto planned PAM modules. |

Internal docs (`docs-fe/docs/internal/` — not published):
[`DECISIONS.md`](docs-fe/docs/internal/DECISIONS.md) (ADRs),
[`ROADMAP.md`](docs-fe/docs/internal/ROADMAP.md),
[`DB_SCALING.md`](docs-fe/docs/internal/DB_SCALING.md),
[`CACHING.md`](docs-fe/docs/internal/CACHING.md),
[`PLATFORM_HARDENING.md`](docs-fe/docs/internal/PLATFORM_HARDENING.md).

## Repository layout

```
api/
  src/
    Bootstrapper/Pam.Api/             ASP.NET host: DI, OTel, rate limit, health
    Modules/<Module>/
      Pam.<Module>/                   aggregates, features, EF, module wire-up
      Pam.<Module>.Contracts/         DTOs, IQuery<T>, integration events
    Shared/
      Pam.Shared/                     DDD primitives, MediatR behaviors, EF interceptors
      Pam.Shared.Contracts/           ICommand/IQuery, IDomainEvent, Actor
      Pam.Shared.Messaging/           MassTransit wire-up, outbox reconciler, MessagingOutboxOptions
  tests/
    Pam.<Module>.UnitTests/           xUnit v3 pure-domain tests
    Pam.IntegrationTests/             Testcontainers-backed end-to-end
    Pam.ArchitectureTests/            NetArchTest module-boundary rules
  Makefile                            build / test / run / migrations
tests/
  stress/                             k6 scripts (21g.js, fastspin.js)
docs-fe/                              Docusaurus site (published + internal docs)
Makefile                              stress workflows (root-level)
docker-compose.yml                    full local stack (mssql, rabbit, redis, otel-lgtm, seq, mailpit)
```

## Adding a feature

`Pam.Operators` is the canonical reference for the per-module pattern;
`Pam.Ingest` is the reference for the vendor-adapter pattern. Each
feature ships as four files in
`Modules/<X>/Pam.<X>/<Aggregate>/Features/<UseCase>/`:

- `<UseCase>Command.cs` — what the API receives (or `Query.cs` for reads)
- `<UseCase>Validator.cs` — FluentValidation rules
- `<UseCase>Handler.cs` — the actual work
- `<UseCase>Endpoint.cs` — Carter route + OpenAPI annotations (every
  endpoint must follow the annotation chain in
  [`api/CLAUDE.md`](api/CLAUDE.md))

MediatR + FluentValidation + Carter all auto-discover via assembly
scanning — no manual DI registration. Full walkthrough in
[`docs-fe/docs/LOCAL_DEV.md`](docs-fe/docs/LOCAL_DEV.md#adding-a-feature).
