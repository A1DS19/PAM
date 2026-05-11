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

Scaffolded as of 2026-05-11. Phase A (intercept-and-forward to GBS) is
the next implementation step.

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
| `DATETIME` (no TZ) | `timestamptz` via `DateTimeOffset` |
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
  occurred_at          timestamptz NOT NULL       -- vendor-reported event time
  received_at          timestamptz NOT NULL       -- when we wrote it (IClock.UtcNow)
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
   concurrently. `DbUpdateException` with Postgres `SQLSTATE 23505` →
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
use `(ReceivedAt - OccurredAt)`. Both stored as `timestamptz`; neither
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

## What's built (today, scaffold)

| Piece | Status |
|---|---|
| `VendorTransaction` aggregate (append-only, signed cents) | ✓ |
| EF mapping + indexes + UNIQUE constraint | ✓ |
| Initial migration (`vendor_transactions` + outbox tables) | ✓ |
| `IngestTransactionCommand` / Validator / Handler | ✓ |
| Idempotency (fast-path + race path) | ✓ |
| `TransactionIngestedDomainEvent` + bridge handler | ✓ |
| `TransactionIngestedIntegrationEvent` (outbox-ready) | ✓ |
| Outbox configured (`ConfigureOutbox` delegate) | ✓ |
| `IVendorAdapter` interface | ✓ |
| 21G stub adapter + endpoint (`/v1/ingest/vendors/21g`) | ✓ |
| 2 placeholder unit tests | ✓ |
| Wired into `Pam.Api/Program.cs` | ✓ |

## What's NOT built (intentional, sequence-gated)

| Missing | Trigger |
|---|---|
| Real 21G SOAP listener (stub accepts JSON) | Phase A start |
| Other vendor adapters (BTCasino, Vegas, WNet, Pocket) | Phase A per-vendor rollout |
| Real vendor auth (HMAC, IP allow-list, etc.) | Per-vendor adapter implementation |
| `IPlayerLookup` from `Pam.Players.Contracts` | Players module #3 |
| `IGbsRelay` forwarder for Phase A | Phase A first PR |
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
  IngestModule.cs                                        AddIngestModule + ConfigureOutbox
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

## Smoke test

```bash
# 1. Apply the migration
make migrate-update MODULE=Ingest

# 2. Send a vendor transaction
curl -X POST http://localhost:5000/v1/ingest/vendors/21g \
  -H 'Content-Type: application/json' \
  -H 'X-Vendor-System-Id: demo' \
  -d '{
    "brandId":   "00000000-0000-0000-0000-000000000001",
    "playerId":  "00000000-0000-0000-0000-000000000002",
    "reference": "vendor-tx-001",
    "amountCents": 1500,
    "currency":  "USD",
    "kind":      0,
    "occurredAt": "2026-05-11T17:00:00Z"
  }'
# → 200 { transactionId, status: "OK" }

# 3. Retry the same request (vendor's idempotent retry)
curl -X POST http://localhost:5000/v1/ingest/vendors/21g <same body>
# → 200 { transactionId: <same id>, status: "DUPLICATE" }

# 4. Inspect the row
docker exec -it pam-postgres psql -U pam -d pam -c \
  "SELECT vendor_id, vendor_reference, amount_cents, kind, status, occurred_at \
   FROM ingest.vendor_transactions ORDER BY received_at DESC LIMIT 5;"

# 5. Inspect the outbox-queued integration event
docker exec -it pam-postgres psql -U pam -d pam -c \
  "SELECT enqueue_time, message_type FROM ingest.outbox_message ORDER BY enqueue_time DESC LIMIT 5;"
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the broader outbox + domain
event pattern this module follows, and
[DECISIONS.md](DECISIONS.md) #25 for the strangler-fig phasing rationale.
