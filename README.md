# PAM — Player Admin Manager

A regulated iGaming back-office system. Built as a .NET 10 modular
monolith with an explicit path to extracting modules into services when
scale or team-org demands it.

## Status

Single module (`Pam.Players`) with one feature: `POST /v1/auth/register`.
Multi-brand foundation in place (Brand → ZITADEL Org). Everything else
(Wallets, KYC, Limits, Bonuses, Bets, Operators, Audit, CMS, Reporting) is
deferred. See [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Stack

- **.NET 10** · ASP.NET Core, EF Core 10, Postgres 17
- **Identity:** ZITADEL (Org-per-Brand; player audience today, operator
  audience pending)
- **Endpoints:** Carter (vertical slices), Scalar OpenAPI in dev
- **Mediator + validation:** MediatR (held at 12.4.1, last MIT) +
  FluentValidation 12
- **Messaging:** MassTransit 8.5.9 + RabbitMQ (outbox pending)
- **Observability:** Serilog (Seq optional), OpenTelemetry tracing /
  metrics / EF instrumentation
- **Tests:** xUnit v3 + FluentAssertions 7.2.0
- **Analyzers:** Meziantou, SonarAnalyzer, BannedApiAnalyzers
  (warnings-as-errors)

Central package management via `Directory.Packages.props`. License-driven
pins (MediatR 12.4.1, MassTransit 8.5.9, FluentAssertions 7.2.0) are
documented in [`docs/DECISIONS.md`](docs/DECISIONS.md).

## Quick start

```bash
make up                              # Postgres, ZITADEL, RabbitMQ, etc. + bootstrap
make migrate-update MODULE=Players   # apply EF migrations
make run                             # dotnet watch
make test                            # 22 unit tests, ~40ms
```

API at `http://localhost:5000`. Scalar UI at `/scalar/v1`. ZITADEL console
at `http://localhost:8080`. Smoke test in
[`docs/LOCAL_DEV.md`](docs/LOCAL_DEV.md).

## Documentation

| Doc | Purpose |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | The **why** — module boundaries, identity model, brand model, audit, error model, time, secrets, domain vs integration events, aggregate sizing rules. |
| [`docs/LOCAL_DEV.md`](docs/LOCAL_DEV.md) | First-time setup, service ports, EF migration commands, feature template, secrets, OpenAPI/Scalar. |
| [`docs/ROADMAP.md`](docs/ROADMAP.md) | Deferred work and the trigger that brings each forward. |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | ADR-style log of architectural decisions. |
| [`docs/CORE_PLATFORM_MAPPING.md`](docs/CORE_PLATFORM_MAPPING.md) | Section-by-section mapping of the legacy "Core Platform" feature surface onto planned PAM modules, with the "10x better" decisions. |

## Repository layout

```
src/
  Bootstrapper/Pam.Api/          ASP.NET host: DI, auth, OTel, rate limit, health
  Modules/<Module>/
    Pam.<Module>/                aggregates, features, infra (per-module)
    Pam.<Module>.Contracts/      public integration events + read-model interfaces
  Shared/
    Pam.Shared/                  DDD primitives, MediatR behaviors, EF interceptors,
                                 IClock, IUserContext, exceptions
    Pam.Shared.Contracts/        ICommand/IQuery, IDomainEvent, Actor, PamIds
    Pam.Shared.Messaging/        MassTransit registration, IntegrationEvent base
tests/
  Pam.Players.UnitTests/         pure-domain tests on xUnit 3
infra/
  zitadel/bootstrap.sh           idempotent ZITADEL Org/Project setup
docs/                            see table above
```

## Adding a feature

The `Register` feature in
`src/Modules/Players/Pam.Players/Players/Features/Register/` is the
canonical template. Five files per feature: command, validator, handler,
endpoint, optional domain-event handler. MediatR + FluentValidation +
Carter all auto-discover via assembly scanning — no manual DI
registration. Full walkthrough in
[`docs/LOCAL_DEV.md`](docs/LOCAL_DEV.md#adding-a-feature).
