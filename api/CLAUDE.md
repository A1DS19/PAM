# PAM project conventions

This file is loaded automatically by Claude Code on every session. It
captures the conventions that aren't enforced by analyzers / arch tests
and that Claude would otherwise drift on. Read once, follow always.

**Trust order**: this file → `../docs-site/docs/ARCHITECTURE.md` →
`../docs-site/docs/internal/DECISIONS.md` ADRs → other topical docs in
`../docs-site/docs/` (published) and `../docs-site/docs/internal/`
(internal-only). If something here contradicts a doc, the doc wins —
update this file.

---

## Endpoint annotations are non-negotiable

Every Carter endpoint MUST chain the following annotations. The order
matters only for readability; all of them must be present:

```csharp
app.MapPost("/v1/...", handler)
    .WithTags("<Module>")                          // one tag = module name
    .WithName("<UniqueOperationId>")
    .WithSummary("<verb-led one-liner, no trailing period>")
    .WithDescription(
        """
        <markdown body — see template below>
        """
    )
    .Accepts<TRequest>("application/json")       // POST/PUT/PATCH
    .Produces<TResponse>(StatusCodes.Status200OK) // or Status201Created etc.
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status400BadRequest)   // when applicable
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden)    // when auth-protected
    .ProducesProblem(StatusCodes.Status404NotFound)     // when applicable
    .ProducesProblem(StatusCodes.Status409Conflict)     // when applicable
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity) // when applicable
    .ProducesProblem(StatusCodes.Status429TooManyRequests)     // when rate-limited
    .RequireAuthorization($"Permissions.{PermissionCodes.X}")
    // OR: .AllowAnonymous().RequireRateLimiting("auth-sensitive")
```

**Why**: Scalar (the OpenAPI UI) shows a rich card per endpoint —
summary in the list view, full description on click, every possible
status code, the request/response schemas. Skipping any of these makes
the endpoint look incomplete in Scalar and degrades the back-office
team's ability to self-serve.

### Summary writing rules

- One short sentence, under ~80 characters.
- Sentence case. No trailing period.
- Verb-led: "Create a back-office user", "List players with filtering",
  "Reset a player's MFA enrollment".

### Description writing rules

Triple-quoted raw string. Markdown is rendered. Use **bold** labels for
the structured sections:

```text
"""
<one-paragraph plain-English description of what the endpoint does>

**Auth:** required permission OR "anonymous, rate-limited by ...".

**Idempotency:** explicit one-liner if the endpoint has retry/duplicate
semantics; omit if not applicable.

**Side effects:** call out anything that changes state beyond the
obvious (sends email, rotates security stamp, revokes tokens, fires
integration events).

**Status codes:**
- `200 OK` — ...
- `400 Bad Request` — ...
- `401 Unauthorized` / `403 Forbidden` — auth failed.
- `409 Conflict` — ...
"""
```

Derive content from the leading `//` comment block on each endpoint
file — most files already document this in C# comments; promote it to
the OpenAPI description.

### Request and response DTOs MUST be public records

If OpenAPI can't reach the type, it can't render the schema. Means:

- Request bodies: top-level public records in the same file (or a
  sibling `*Contracts.cs` file) as the endpoint, NOT private nested
  types inside handlers or adapters.
- Response bodies: same. Don't return anonymous types from endpoints —
  the `.Produces<T>()` declaration will lie.

The 21G vendor endpoint (`Pam.Ingest/Vendors/TwentyOneG/`) is the
reference for public DTOs + adapter pattern.

---

## Module boundary rules

Enforced by `NetArchTest.Rules` in `tests/Pam.ArchitectureTests/` — but
also follow them when writing new code, not just when fixing red tests.

1. **A `Pam.<X>` project never references another `Pam.<Y>` project
   directly.** The `Pam.<Y>.Contracts` assembly is the only allowed
   seam.
2. **Cross-module communication is integration events (RabbitMQ) or
   `IQuery<T>` (in-process).** Never direct DbContext access; never
   types from another module's internal namespace.
3. **Domain events stay in `Pam.<Module>/.../Events/`.** Integration
   events stay in `Pam.<Module>.Contracts/.../IntegrationEvents/`.
4. **Aggregates extend `Aggregate<TId>`** (from `Pam.Shared.DDD`). Plain
   entities extend `Entity<TId>`. Records that travel on the wire don't
   extend anything — they're DTOs.

See `../docs-site/docs/ARCHITECTURE.md` "Domain events vs integration
events" and "Modular monolith" sections for the full reasoning.

---

## Per-module shape

Every module ships as exactly three projects (ADR #19):

```
src/Modules/<X>/
  Pam.<X>/                    aggregates, features, EF, wire-up (private)
  Pam.<X>.Contracts/          DTOs, IQuery<T>, integration events (public)
tests/
  Pam.<X>.UnitTests/          aggregate + validator unit tests
```

Inside `Pam.<X>/`:

```
<Aggregate>/
  Models/<Aggregate>.cs
  Features/<UseCase>/
    <UseCase>Command.cs       what the API receives
    <UseCase>Validator.cs     FluentValidation rules
    <UseCase>Handler.cs       the actual work
    <UseCase>Endpoint.cs      Carter route + OpenAPI annotations
  Events/<DomainEvent>.cs
  EventHandlers/<DomainEvent>DomainHandler.cs  ← bridge → integration event
  Exceptions/<Aggregate>Errors.cs               ← stable string error codes
Data/
  <Module>DbContext.cs                          ← schema "<module>"
  <Module>DbContextDesignTimeFactory.cs
  Configurations/<Aggregate>Configuration.cs
  Migrations/
<Module>Module.cs                               ← AddXModule + UseXModuleAsync
```

`Pam.Operators` is the reference for the full pattern. `Pam.Ingest` is
the reference for the vendor-adapter pattern.

---

## Money and time

Two hard rules. Both have analyzer + code review enforcement; do not
work around either.

### Money is `long` signed cents

- Database column type is `bigint`, never `float` / `double` /
  `numeric`.
- Risk (debit) is negative; Win (credit) is positive. The vendor
  adapter applies the sign; the handler does NOT infer sign from
  `Kind`.
- ISO 4217 currency code in a `char(3)` column.

GBS uses `FLOAT` cents — that's one of the explicit bugs PAM is fixing.
Don't carry it forward.

### Time is `DateTimeOffset`, stored as `datetimeoffset`

- Use `IClock.UtcNow` in any code path you'd want testable. Never call
  `DateTime.Now`, `DateTime.UtcNow`, or `DateTimeOffset.Now` — they're
  banned via `BannedSymbols.txt`.
- `DateTimeOffset.UtcNow` is allowed only in places where DI is
  impractical (e.g. record init defaults).
- Every timestamp column is `datetimeoffset` (SQL Server's
  timezone-aware type — equivalent to Postgres `timestamptz` from the
  pre-ADR-#27 era). Never naive `datetime`/`datetime2` without offset.

---

## Idempotent commands and exception mapping

- Stable error codes are string constants in `Pam.<Module>/<Aggregate>/
  Exceptions/<Aggregate>Errors.cs`. Naming: `<module>.<aggregate>.<verb>`
  (e.g. `operators.brand.slug-taken`).
- Exceptions inherit from `PamDomainException` (constructor:
  `(string code, string message)`). The `CustomExceptionHandler` maps
  them to RFC-7807 `ProblemDetails` with `extensions.code = <stable code>`.
- For endpoints that vendors / clients retry: use a database UNIQUE
  constraint as the idempotency anchor. Catch `DbUpdateException` for
  SQL Server error number 2627 or 2601 (unique-key violations) and surface a `Duplicate` outcome. See
  `Pam.Ingest/Transactions/Features/Ingest/IngestTransactionHandler.cs`
  for the canonical shape.

---

## Outbox and events

- **One shared outbox DbContext, period.** `PamMessagingDbContext`
  (schema `messaging`, in `Pam.Shared.Messaging`) owns the MassTransit
  `inbox_state` / `outbox_state` / `outbox_message` tables. The single
  `UseBusOutbox()` call in `AddPamMassTransit` binds it as the bus-wide
  outbox target. Module DbContexts do NOT carry outbox entities and
  modules do NOT call `AddEntityFrameworkOutbox` themselves. Why this
  shape: MT 8.5.x cannot multiplex the outbox across DbContexts on a
  single bus — see ADR #26 for the citation + the atomicity trade-off
  we currently accept. Don't try to add `ConfigureOutbox` to a new
  module; the bus-wide registration already covers it.
- Adding a new publisher = define a domain event aggregate-side +
  bridge handler that calls `IPublishEndpoint.Publish` with an
  integration event. `OutboxFlushBehavior` (innermost MediatR
  behavior) commits the outbox row at command-tail.
- Domain handlers run **pre-save** (inside the business DbContext's
  `SaveChanges` transaction). A throwing handler rolls back the
  business write. Don't put best-effort side effects (logging,
  metrics, fire-and-forget calls) in a domain handler — put them in
  an integration-event consumer.

See `../docs-site/docs/ARCHITECTURE.md` "Outbox + pre-save domain
dispatch" for the full trade-off discussion and
`../docs-site/docs/internal/DECISIONS.md` ADR #26 for the
multi-module-outbox topology rationale.

---

## Auditing

Every `ICommand` is automatically audited via `AuditBehavior` (a MediatR
pipeline behavior). Rows land in `audit.command_log` with actor,
correlation id, payload JSON (sensitive fields redacted by
`SensitiveJsonRedactor`), timing, and success/failure.

Queries (`IQuery<T>`) are NOT audited — only commands.

If a new command carries fields that need redaction (passwords, tokens,
PII), add the field name to `SensitiveJsonRedactor`'s allowlist.

**High-volume commands opt out via `IUnauditedCommand`.** When a command
runs at vendor-ingest volume (millions/day) AND its business row already
carries the same actor / payload / timing / status, marker-interface it
with `IUnauditedCommand` so `AuditBehavior` short-circuits.
`IngestTransactionCommand` is the canonical example —
`ingest.vendor_transactions` is the audit trail; a 1:1 audit row would
bloat storage with no new investigative value. Failures still land in
`LoggingBehavior` (Seq) and `OpenTelemetryBehavior`, so the failure
trail is intact.

---

## ID generation

Always `Guid.CreateVersion7()` via the `PamIds.New()` helper from
`Pam.Shared.Contracts.Identity`. UUIDv7 is time-ordered so it indexes
efficiently as a primary key in SQL Server. Don't use `Guid.NewGuid()`
(random v4) for primary keys; reserve it for nonce-style use.

---

## Caching

Opt-in per query via attributes on `IQuery<T>` types:

```csharp
[Cache(durationMinutes: 5, keyPattern: "identity:user:{UserId}")]
public sealed record GetUserQuery(Guid UserId) : IQuery<BackOfficeUserDto>;
```

And commands that should invalidate after success:

```csharp
[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record UpdateUserCommand(...) : ICommand;
```

The `CachingBehavior` handles the round-trip. See
`../docs-site/docs/internal/CACHING.md`.

Don't cache:
- Auth tokens / sessions (they're already cached by OpenIddict).
- Anything containing per-user sensitive data with a long TTL.

---

## Test conventions

- New aggregate → behavior tests in `tests/Pam.<X>.UnitTests/` (xUnit v3
  + FluentAssertions 7.2.0 — both pinned; don't bump).
- New endpoint → validator test in unit tests. Endpoint-level
  integration tests live in `tests/Pam.IntegrationTests/` and need a
  test-only auth seam (still pending).
- New cross-module behavior → architecture rule in
  `tests/Pam.ArchitectureTests/`.

xUnit v3 quirks:
- Use `TestContext.Current.CancellationToken` for async tests
  (xUnit1051 enforces).
- `HttpClient.GetAsync(Uri uri)`, not `string` (CA2234).
- `await using var factory = new PamApiFactory(containers);` — one per
  test method.

Run: `make test`.

---

## Dependency licensing pins (do not bump)

These libraries are pinned at their last permissively-licensed release
(ADR #5). Bumping them requires legal review:

| Package | Pinned version | Reason |
|---|---|---|
| MediatR | 12.4.1 | Last MIT — v13+ Lucky Penny commercial |
| MassTransit (+ rabbitmq + EF) | 8.5.9 | Last Apache-2.0 of v8 — v9 Massient commercial |
| FluentAssertions | 7.2.0 | Last Apache-2.0 — v8+ Xceed commercial |

---

## When in doubt

Published docs (rendered on the Docusaurus site, live in
`../docs-site/docs/`):

- Deep architectural reasoning → `ARCHITECTURE.md`.
- Module-specific topical → `AUTH.md` (Identity), `INGEST.md`,
  `CORE_PLATFORM_MAPPING.md`, `TESTING.md`, `ENDPOINTS.md`.
- "How do I run it?" → `LOCAL_DEV.md`.

Internal-only docs (live in `../docs-site/docs/internal/`):

- Why-and-when of a specific decision → `DECISIONS.md` (20+ ADRs,
  reverse-chronological).
- `CACHING.md`, `DB_SCALING.md`, `PLATFORM_HARDENING.md`.
- "Is it done?" → `ROADMAP.md`.
- May 12 presentation → `PRESENTATION.md`.
