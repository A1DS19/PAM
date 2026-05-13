# Ingest

The `Pam.Ingest` module is the transaction-intercept layer for third-party
vendors (casinos, lottos, third-party sportsbooks, horse-racing,
cashier). It's the foundation for the CTO doc's **CS Part 1 — Unified
Transaction View** requirement and the eventual data substrate for
financial reporting + the strangler-fig migration off GBS.

**Scope.** One vendor-agnostic write path. Every casino vendor's
callback funnels through a vendor-specific adapter into a single
canonical `VendorTransaction` row in `ingest.vendor_transactions`.
Vendor-side reconciliation, fraud screening, and downstream effects
(wallet posting, notifications, audit, reporting) consume the resulting
`TransactionIngestedIntegrationEvent` over RabbitMQ.

Scaffolded 2026-05-11. The 21G SoapCore listener landed and was
end-to-end smoke-tested on 2026-05-12 (see *Phase A SOAP listener —
2026-05-12 status* below); the outbox publish path was diagnosed,
refactored to a single shared `PamMessagingDbContext`, and re-verified
the same day (see ADR #26). The `IGbsRelay` forwarder, real vendor
auth, and the schema cleanup pass (nullable `BrandId` / `PlayerId` +
new vendor-identity columns) remain the blockers before Phase A goes
live in front of real vendor traffic.

---

## The thesis: GBS's data model is fine; the integration layer isn't

The legacy GBS exploration (`~/Desktop/work/gbs/`) showed that `tbCasinoPlayToday` is actually a clean shared table — one row per
transaction, vendor identified by a `SystemID` column, vendor's
transaction id stored in `Reference` as the idempotency key. The mess
isn't in the data model — it's in the **integration layer**:

- One bespoke controller per vendor (21G SOAP, BTCasino REST+HMAC-MD5,
  others differ again). No shared abstraction.
- Authentication is all over the map (plaintext systemId+password,
  HMAC-MD5, IP allow-list, bearer tokens by vendor).
- Money stored as `FLOAT` cents — float on monetary values is a known
  defect class.
- `DATETIME` columns without timezone offset — root cause of Roger's
  Databricks reconciliation mismatches.
- "Non-posted" vs "posted" is a UX artifact (manual CS posting) that
  GBS conflates with storage state.

PAM carries the clean parts forward (single table, `SystemID` discriminator,
`Reference`-based idempotency) and fixes the broken parts:

| GBS | PAM |
|---|---|
| `FLOAT` cents | `bigint` signed cents |
| `DATETIME` (no TZ) | `datetimeoffset` via `DateTimeOffset` |
| Per-vendor controller, no abstraction | `IVendorAdapter` interface, one per vendor |
| Auth varies wildly | Auth handled inside the adapter, behind one seam |
| "Non-posted" baked into table state | `TransactionStatus` enum with explicit lifecycle |
| Casino transactions on separate UI tab | Single canonical query for the unified transaction view |

---

## Data model (`ingest.vendor_transactions`)

```
ingest.vendor_transactions
  id                   uuid PK
  vendor_id            varchar(32) NOT NULL       -- "21g", "btcasino", "vegas", ...
  vendor_reference     varchar(400) NOT NULL      -- the vendor's transaction id
  brand_id             uuid NOT NULL
  player_id            uuid NOT NULL              -- PAM PlayerId (post-Players)
  amount_cents         bigint NOT NULL            -- SIGNED. Risk negative, Win positive.
  currency             char(3) NOT NULL           -- ISO 4217
  kind                 varchar(16) NOT NULL       -- Risk | Win | Refund | Bonus | Correction
  status               varchar(16) NOT NULL       -- Received | Posted | Duplicate | Rejected
  round_id             varchar(200) NULL          -- vendor-supplied; groups per-game-round events
  description          varchar(250) NULL
  occurred_at          datetimeoffset NOT NULL       -- vendor-reported event time
  received_at          datetimeoffset NOT NULL       -- when we wrote it (IClock.UtcNow)
  rejected_reason      varchar(64) NULL
  -- audit columns (created_at, created_by_type, created_by_id, last_modified_*)
  UNIQUE (vendor_id, vendor_reference)            -- ix_vendor_transactions_idempotency

Indexes:
  ix_vendor_transactions_idempotency       (vendor_id, vendor_reference) UNIQUE
  ix_vendor_transactions_player_timeline   (brand_id, player_id, occurred_at DESC)
  ix_vendor_transactions_vendor_timeline   (vendor_id, occurred_at DESC)
```

The `player_timeline` index serves the CS unified view query:

```sql
SELECT id, vendor_id, kind, amount_cents, currency, status, occurred_at
FROM ingest.vendor_transactions
WHERE brand_id = $1 AND player_id = $2
ORDER BY occurred_at DESC
LIMIT 100;
```

That's the entire "unified transaction view" — one query, every vendor.

---

## The integration shape

Each vendor's callback flows through three layers:

```
1. Vendor endpoint                /v1/ingest/vendors/{vendor-code}
                                  Anonymous, rate-limited via "api-default".
                                  No PAM JWT — the vendor isn't a PAM user.

2. IVendorAdapter implementation  Phase-specific to each vendor:
                                    AuthenticateAsync()  → vendor-specific auth
                                    TranslateAsync()     → vendor payload → canonical command
                                    FormatResponseAsync()→ vendor-shaped reply
                                  Lives at Vendors/<Vendor>/<Vendor>Adapter.cs.

3. IngestTransactionHandler       Vendor-agnostic. Idempotency check, persist
                                  the row, raise domain event. Outbox publishes
                                  the integration event atomically with the
                                  row commit.
```

The endpoint is intentionally thin:

```csharp
app.MapPost($"/v1/ingest/vendors/{VendorCodes.TwentyOneG}",
    async (HttpContext ctx, TwentyOneGAdapter adapter, ISender sender, CT ct) =>
{
    if (!await adapter.AuthenticateAsync(ctx.Request, ct)) return Results.Unauthorized();
    var cmd = await adapter.TranslateAsync(ctx.Request, ct);
    if (cmd is null) return Results.BadRequest(...);
    var result = await sender.Send(cmd, ct);
    return await adapter.FormatResponseAsync(result, ctx.Request, ct);
})
.AllowAnonymous()
.RequireRateLimiting("api-default");
```

Adding a new vendor is mechanical: implement `IVendorAdapter`, register
it in `IngestModule.AddIngestModule`, write a Carter `ICarterModule`
endpoint that follows this same five-line shape.

---

## Idempotency

The vendor's `VendorReference` field is the idempotency key. A retry from
the vendor with the same `(vendor_id, vendor_reference)` returns
`TransactionStatus.Duplicate` and the original row's id — not a fresh
insert.

The handler does two things:

1. **Fast path**: a `WHERE vendor_id = ? AND vendor_reference = ?` lookup
   short-circuits the common case (vendor's retry timer fires before our
   first response gets back). One PK-served index read.
2. **Race path**: the UNIQUE index on `(vendor_id, vendor_reference)`
   catches the rare case where two replicas process the same retry
   concurrently. `DbUpdateException` with SQL Server error 2627/2601 (unique-key violation) →
   re-fetch and return Duplicate.

```csharp
catch (DbUpdateException ex) when (IsUniqueViolation(ex))
{
    db.ChangeTracker.Clear();
    var raced = await db.VendorTransactions.AsNoTracking()
        .Where(t => t.VendorId == cmd.VendorId
                 && t.VendorReference == cmd.VendorReference)
        .Select(t => new { t.Id })
        .FirstAsync(cancellationToken);
    return new IngestTransactionResult(raced.Id, TransactionStatus.Duplicate);
}
```

Vendors that retry forever (most do) get a stable answer for free.

---

## Money and time

**Amounts.** Signed `bigint` cents. Never `decimal`, never `double`.
The aggregate factory expects the caller (adapter) to apply the sign —
PAM doesn't infer sign from `Kind` because vendors disagree on sign
convention. The 21G adapter, for example, flips negative for `Risk` and
positive for everything else; BTCasino sends signed values already.

**Time.** Two distinct timestamps per row:

- `OccurredAt` — vendor-reported event time. Clock skew, network delay,
  and retry behavior all show up here.
- `ReceivedAt` — what `IClock.UtcNow` returned when we wrote the row.

Daily-figure reports use `OccurredAt` (business time); SLA / lag metrics
use `(ReceivedAt - OccurredAt)`. Both stored as `datetimeoffset`; neither
silently truncates timezone. Roger's Databricks reconciliation mismatch
goes away the day Phase A starts capturing.

---

## Status lifecycle

```
                          ┌─→ Received   ←─ initial state, balance not yet applied
[vendor callback]  ───────┤
                          ├─→ Duplicate  ←─ (vendor_id, vendor_reference) already exists
                          └─→ Rejected   ←─ validation failed (unknown player/currency/...)
                                  │
                                  │  (Phase C+, when PAM owns the wallet)
                                  ▼
                              Posted     ←─ wallet authorized + applied
```

In Phase A (intercept-and-forward), every successful ingest stays at
`Received` indefinitely — GBS owns the wallet, so PAM never transitions
the row to `Posted`. The status field is forward-compatible with
Phase C+ when PAM becomes the wallet authority.

Rejected transactions persist as audit records (operators need to see
attempted bad submissions) but raise no integration event — there's
nothing downstream to react to.

---

## Strangler-fig phases for the GBS casino migration

**Phase A — Intercept-and-forward.** PAM exposes the vendor endpoint at
its own URL. The adapter normalizes the request and PAM persists a
`Received` row. PAM then forwards the original request to GBS's existing
endpoint and returns GBS's response verbatim to the vendor. **Zero
functional change to GBS.** PAM gains a clean parallel stream of every
transaction with proper timezones and signed cents.

**Phase B — Emit integration events.** `Pam.Ingest` publishes
`TransactionIngestedIntegrationEvent` for each row. Downstream consumers
land:

- Roger's "Simplified Financial Reporting" — Databricks reads our event
  stream instead of GBS's messy joins. Mismatched-timestamp problem
  disappears.
- `Pam.Notifications` consumes for "transaction posted" emails (vendor
  emails go through PAM's templating, not GBS's hardcoded strings).
- `Pam.Audit` already captures every command into `audit.command_log`.

**Phase C — PAM becomes authoritative for one vendor.** Pick the
lowest-traffic one (probably Pocket or 21G). PAM stops forwarding; PAM
either calls the existing GBS stored proc directly to post to the GBS
wallet OR (Phase C') writes through to `Pam.Wallet` once it exists.
GBS's `tbCasinoPlayToday` is kept in sync by a one-way job from
`ingest.vendor_transactions` so Crystal Reports keep working.

**Phase D — All vendors migrated, GBS casino write path retired.**
`tbCasinoPlayToday` becomes a read-only view fed from
`ingest.vendor_transactions`. Crystal Reports keep working off the
view. When Roger's "Listo" milestone arrives, the view goes too.

The four phases are deliberately shippable independently. A single
vendor can be at Phase C while five others are at Phase A.

---

## Route convention

```
POST /v1/ingest/vendors/{vendor-code}
```

`{vendor-code}` matches `VendorCodes` constants in
`Pam.Ingest.Contracts`. The endpoint:

- `.AllowAnonymous()` — the adapter handles vendor auth.
- `.RequireRateLimiting("api-default")` — sliding window, 100 req/30s
  per partition key, Redis-backed (shared across replicas).
- `.WithTags("Ingest")` — single tag, grouped with the rest of Ingest in
  Scalar's nav. (Per-vendor sub-tags deliberately not used — they'd
  show each endpoint twice in the nav.)
- Full OpenAPI annotation chain per [`ENDPOINTS.md`](ENDPOINTS.md):
  `WithSummary` + `WithDescription` (markdown body) +
  `Accepts<TRequest>` + `Produces<TResponse>` + every
  `ProducesProblem` the endpoint can return.

Each vendor gets its own `ICarterModule` so the route + adapter +
auth stay co-located. Request and response DTOs are public records
in a sibling `<Vendor>Contracts.cs` file so OpenAPI/Scalar can render
their schemas (private nested types don't work — OpenAPI can't reach
them).

---

## What's built

| Piece | Status |
|---|---|
| `VendorTransaction` aggregate (append-only, signed cents) | ✓ |
| EF mapping + indexes + UNIQUE constraint | ✓ |
| Initial migration (`vendor_transactions` + outbox tables) | ✓ |
| `IngestTransactionCommand` / Validator / Handler | ✓ |
| Idempotency (fast-path + race path) | ✓ |
| `TransactionIngestedDomainEvent` + bridge handler | ✓ |
| `TransactionIngestedIntegrationEvent` (DTO) | ✓ |
| Outbox publish path (bus-wide outbox on `PamMessagingDbContext`) | ✓ end-to-end verified 2026-05-12 |
| `IVendorAdapter` interface + 21G JSON stub | ✓ kept as reference for the adapter pattern |
| **21G SoapCore listener at GBS URL paths** | ✓ end-to-end smoke-tested 2026-05-12 |
| `TwentyOneGReferenceHasher` (SHA-256 content idempotency, signed cents, `yyyyMMdd` parsing) | ✓ |
| WSDL captures in `infra/wsdl/21g/` (production GBS host) | ✓ |
| 2 placeholder unit tests | ✓ |
| Wired into `Pam.Api/Program.cs` (SoapCore middleware ahead of auth) | ✓ |

## What's NOT built (intentional, sequence-gated)

| Missing | Trigger |
|---|---|
| Shared-connection-shared-transaction atomicity between business + outbox saves (or upgrade to MT 9.1 multi-DbContext outbox) | When the under-deliver window stops being acceptable; reconciliation job lands as a defensive backstop alongside |
| `IGbsRelay` forwarder (intercept-and-forward proxy) | Required before flipping vendor DNS to PAM |
| Real vendor auth (validate `systemID` + `systemPassword` against `ingest.vendor_credentials`) | Before live 21G traffic |
| `IPlayerLookup` from `Pam.Players.Contracts` | Players module #3 |
| Schema cleanup: nullable `BrandId` / `PlayerId`; `vendor_customer_id`, `daily_figure_date`, `gbs_reference` columns | Once Players + IGbsRelay exist |
| Other vendor adapters (BTCasino, Vegas, WNet, Pocket) | Phase A per-vendor rollout |
| `GET /v1/ingest/transactions?brandId=&playerId=` (unified view query) | When the CS UI consumes it |
| Brand-scoped global query filter | First authenticated player endpoint |
| Vendor reconciliation jobs (catch-up missed callbacks) | When a vendor drops more than transient retries |
| Reference data sync (`ingest.vendor_transactions` → GBS `tbCasinoPlayToday`) | Phase C |

---

## Implementation map

```
src/Modules/Ingest/Pam.Ingest.Contracts/
  Vendors/VendorCodes.cs                                 well-known vendor strings
  Transactions/Models/TransactionKind.cs                 Risk | Win | Refund | Bonus | Correction
  Transactions/Models/TransactionStatus.cs               Received | Posted | Duplicate | Rejected
  Transactions/IntegrationEvents/
    TransactionIngestedIntegrationEvent.cs               lean, IDs only

src/Modules/Ingest/Pam.Ingest/
  IngestModule.cs                                        AddIngestModule + UseIngestSoapEndpoints
  Transactions/
    Models/VendorTransaction.cs                          aggregate, append-only
    Events/TransactionIngestedDomainEvent.cs             in-module fact
    EventHandlers/
      TransactionIngestedDomainHandler.cs                bridge → integration event
    Exceptions/IngestErrors.cs                           stable error codes
    Features/Ingest/
      IngestTransactionCommand.cs                        canonical command
      IngestTransactionValidator.cs                      format / range checks
      IngestTransactionHandler.cs                        idempotent persist
  Vendors/
    IVendorAdapter.cs                                    auth + translate + format
    TwentyOneG/
      TwentyOneGAdapter.cs                               stub
      TwentyOneGEndpoint.cs                              POST /v1/ingest/vendors/21g
  Data/
    IngestDbContext.cs                                   schema "ingest" + outbox
    IngestDbContextDesignTimeFactory.cs
    Configurations/VendorTransactionConfiguration.cs     mapping + indexes
    Migrations/

tests/Pam.Ingest.UnitTests/
  VendorTransactionTests.cs                              aggregate factory behavior
```

## Phase A SOAP listener — 2026-05-12 status

The 21G SoapCore listener was built and smoke-tested end-to-end today.
This section is the durable record of what was verified, what's open,
and exactly how to reproduce.

### What's wired

```
src/Modules/Ingest/Pam.Ingest/
  Vendors/TwentyOneG/Soap/
    TwentyOneGSoapDefaults.cs                          paths + XML namespace
    TransactionResult.cs                                response DTO ([XmlType ns="http://tempuri.org/"])
    ITwentyOneGCustomerTransactionService.cs           CustomerTransaction21G.asmx
    ITwentyOneGValidateSessionService.cs               ValidateSessionID21GCasino.asmx
    ITwentyOneGGetBalanceService.cs                    GetCustomerBalance21GCasino.asmx
    TwentyOneGCustomerTransactionService.cs            Phase-A impl (persist, no relay)
    TwentyOneGValidateSessionService.cs                Phase-A stub
    TwentyOneGGetBalanceService.cs                     Phase-A stub
    TwentyOneGReferenceHasher.cs                       SHA-256 idempotency + parsing helpers

infra/wsdl/21g/
  CustomerTransaction21G.wsdl                          captured from api.betanything.eu
  ValidateSessionID21GCasino.wsdl
  GetCustomerBalance21GCasino.wsdl
  README.md                                            refresh + diff procedure
```

`IngestModule.UseIngestSoapEndpoints(app)` maps SoapCore at the same
URL paths GBS hosts today, so production cut-over is a host swap with
no path changes:

```
/integrations/21GCasino/CustomerTransaction21G.asmx
/integrations/21GCasino/ValidateSessionID21GCasino.asmx
/integrations/21GCasino/GetCustomerBalance21GCasino.asmx
```

`UseIngestSoapEndpoints()` is mounted **before** `UseAuthentication()`
in `Pam.Api/Program.cs`. Vendor SOAP traffic carries no PAM JWT and
the fallback authorization policy would 401 it otherwise; SoapCore
middleware short-circuits on path match and never reaches the auth
pipeline.

### What was verified on 2026-05-12

| Behavior | Result |
|---|---|
| SOAP envelope → SoapCore → `ITwentyOneGCustomerTransactionService.PostTransaction` | ✓ envelope deserialized cleanly |
| MediatR pipeline (validation → audit → handler) | ✓ row persisted, audit row written |
| Signed cents (`tranCode=D` → negative, `tranCode=C` → positive) | ✓ row had `amount_cents = -1050` for a `D` of `10.50` |
| `dailyFigureDate_YYYYMMDD` parsing into `datetimeoffset` | ✓ `occurred_at` populated from the business day |
| SHA-256 content-hash idempotency (vendor reference is not on the wire) | ✓ first call wrote a row in ~250ms |
| Retry of identical envelope returns `Duplicate` | ✓ second call returned `"Duplicate — ignored"` in ~12ms |
| `(vendor_id, vendor_reference)` UNIQUE catches the race path | ✓ (not exercised today, but constraint is in place) |
| `audit.command_log` captures the synthetic `IngestTransactionCommand` | ✓ command row with correlation id |

### Outbox publish path — verified working (2026-05-12)

Originally reported broken on the same day's first pass: every
`outbox_message` table was empty, zero PAM exchanges in RabbitMQ. The
diagnosis turned up a structural MT-8.5.x constraint
(`IScopedBusContextProvider<IBus>` is keyed on bus type, so only one
DbContext can own the bus outbox per bus) and the architectural
remedy: a single shared `PamMessagingDbContext` (schema `messaging`)
that owns the outbox tables across the whole monolith. Full rationale
+ alternatives considered in [DECISIONS.md](DECISIONS.md) ADR #26.

**What landed:**

- New `Pam.Shared.Messaging.Data.PamMessagingDbContext` in schema
  `messaging` owns `inbox_state`, `outbox_state`, `outbox_message`.
- `AddPamMassTransit` (`Pam.Shared.Messaging`) is the sole call site
  for `AddEntityFrameworkOutbox<PamMessagingDbContext>(o => { o.UseSQL Server(); o.UseBusOutbox(); })`.
  No per-module `ConfigureOutbox` delegate, no per-module outbox
  tables.
- `OutboxFlushBehavior` (innermost MediatR pipeline behavior) commits
  the messaging context at command tail so the outbox rows queued by
  the bus filter actually persist.
- Per-module `RemoveOutboxTables` migrations drop the orphaned
  `inbox_state` / `outbox_state` / `outbox_message` from each module's
  schema.

**End-to-end smoke verified.** Sending the same SOAP envelope as
before now produces:

- A `Flushed 2 outbox row(s) to messaging.outbox_message` debug line
  per request in the API log (1 `OutboxMessage` + 1 `OutboxState`).
- A `Pam.Ingest.Contracts.Transactions.IntegrationEvents:TransactionIngestedIntegrationEvent`
  fanout exchange in RabbitMQ (auto-declared on first publish by
  MT's delivery service).
- An empty `messaging.outbox_message` table in steady state — the
  delivery service removes delivered rows. This is the correct
  outbox semantic; verify via the log line or the exchange list, not
  a SELECT against the table.

**Atomicity caveat (carried forward as a follow-up).** The business
`SaveChanges` and the outbox `SaveChanges` run in separate
transactions. A crash between the two leaves the business row
committed but the integration event undelivered. The window is
bounded by request-tail-time (~ms). True cross-context atomicity via
shared `SqlConnection` + shared `IDbContextTransaction` is logged
as the next follow-up in [ROADMAP.md](ROADMAP.md). A reconciliation
job lands as a defensive backstop regardless of which atomicity path
we take.

### Smoke test (SOAP envelope, end-to-end)

```bash
# 1. Apply migrations (idempotent)
make migrate-update MODULE=Ingest

# 2. Confirm the WSDL is being served by SoapCore
curl -s 'http://localhost:5000/integrations/21GCasino/CustomerTransaction21G.asmx?wsdl' \
  | head -40
# Expect: <wsdl:definitions targetNamespace="http://tempuri.org/" ...

# 3. Send a SOAP envelope matching the GBS contract
cat > /tmp/21g-soap.xml <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"
               xmlns:tem="http://tempuri.org/">
  <soap:Body>
    <tem:PostTransaction>
      <tem:systemID>21G</tem:systemID>
      <tem:systemPassword>dev-password</tem:systemPassword>
      <tem:clerkID>auto</tem:clerkID>
      <tem:customerID>cust-001</tem:customerID>
      <tem:amount>10.50</tem:amount>
      <tem:tranCode>D</tem:tranCode>
      <tem:tranType>Bet</tem:tranType>
      <tem:description>roulette spin</tem:description>
      <tem:bettingAdjustmentFlagYN>N</tem:bettingAdjustmentFlagYN>
      <tem:dailyFigureDate_YYYYMMDD>20260512</tem:dailyFigureDate_YYYYMMDD>
      <tem:enteredBy>vendor</tem:enteredBy>
      <tem:paymentBy>cash</tem:paymentBy>
    </tem:PostTransaction>
  </soap:Body>
</soap:Envelope>
EOF

curl -s -X POST 'http://localhost:5000/integrations/21GCasino/CustomerTransaction21G.asmx' \
  -H 'Content-Type: text/xml; charset=utf-8' \
  -H 'SOAPAction: "http://tempuri.org/PostTransaction"' \
  --data-binary @/tmp/21g-soap.xml
# Expect (first call):
#   <RespMessage>Accepted (PAM Phase A — not yet forwarded to GBS)</RespMessage>
# Expect (replay):
#   <RespMessage>Duplicate — ignored</RespMessage>

# 4. Inspect the persisted row
docker exec pam-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Pam_dev_password_123!" -No -d pam -c \
  "SELECT vendor_id, vendor_reference, amount_cents, kind, status, occurred_at \
   FROM ingest.vendor_transactions ORDER BY received_at DESC LIMIT 5;"
# Expect: vendor_id=21g, vendor_reference=<sha256 hash>, amount_cents=-1050,
#         kind=Risk, status=Received, occurred_at=2026-05-12 00:00:00+00.

# 5. Inspect the audit log
docker exec pam-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Pam_dev_password_123!" -No -d pam -c \
  "SELECT correlation_id, command_type, succeeded, duration_ms \
   FROM audit.command_log ORDER BY started_at DESC LIMIT 5;"
# Expect: IngestTransactionCommand row with succeeded=true.

# 6. Confirm the outbox path. The table is EMPTY in steady state —
#    the delivery service removes delivered rows immediately after
#    publish. Verify the publish actually happened by:
#    (a) tail the API log for "Flushed N outbox row(s)"
grep -E "Flushed.*outbox" /tmp/pam-api.log | tail -5
#    (b) confirm the MT-created exchange exists in RabbitMQ
docker exec pam-rabbitmq rabbitmqctl list_exchanges name type \
  | grep "Pam.Ingest.Contracts"
# Expect: a single fanout exchange named
#   Pam.Ingest.Contracts.Transactions.IntegrationEvents:TransactionIngestedIntegrationEvent
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the broader outbox + domain
event pattern this module follows, and
[DECISIONS.md](DECISIONS.md) #25 for the strangler-fig phasing rationale.
