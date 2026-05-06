# Core Platform → PAM mapping

This is the feature-parity reference plus the "10x better" thesis. The full
manual is at `~/Downloads/Core Platform.pdf` (68 pages, end-user BO doc).

## What Core Platform is

A multi-tenant iGaming back-office (sportsbook + casino + bet shops). The
manual is end-user oriented — it describes what an operator sees in the BO
UI, not the underlying API. The functional surface is enormous (~20 top-level
sections, hundreds of entities, embedded website builder, embedded CRM,
embedded affiliate panel).

The system is **partner-scoped** throughout — every entity belongs to a
"Partner" (the white-label brand). Partners own their own product catalog,
currencies, languages, payment providers, website fragments, banners,
translations, and limits.

## Section → module mapping

| Core Platform section | PAM module | Status |
|---|---|---|
| Clients > Main / Notes / Settings | `Pam.Players` | v1 (Register only) |
| Clients > Sessions | `Pam.Players` (Sessions sub-aggregate) | deferred |
| Clients > KYC | `Pam.Kyc` | deferred |
| Clients > Limits & Exclusions / Product Limits | `Pam.Limits` | deferred |
| Clients > Corrections / Deposits / Withdrawals / Account History / Transactions / Payment Info / Payment Settings | `Pam.Wallets` | deferred (most regulated) |
| Clients > Campaigns / Bets | `Pam.Bonuses` + `Pam.Bets` | deferred |
| Clients > Tickets | `Pam.Support` | deferred |
| Clients > Friends | `Pam.Affiliates` (referrals) | deferred |
| Clients > Emails & SMSes / Provider Settings | `Pam.Notifications` + `Pam.Players` | deferred |
| Online Clients (Real Time) | streaming projection over `Pam.Players` sessions | deferred |
| Affiliates + Affiliate admin panel | `Pam.Affiliates` (separate module + own auth/admin UI) | deferred |
| Users & Agents + Roles | `Pam.Operators` + ZITADEL operators audience | deferred |
| Payments (global) | `Pam.Wallets` query side | deferred |
| Bets / Internet Bets / Bet Shops | `Pam.Bets` + `Pam.BetShops` | deferred |
| Bonuses (Common + Triggers + Conditions DSL) | `Pam.Bonuses` | deferred (own design pass) |
| Segments | `Pam.Segments` (cross-cutting query/projection) | deferred |
| Partners (Main / Payment / Product / Currency / Language / Provider / Limits / Country / Comp Points / Payment Info / Keys) | `Pam.Partners` | **needs to land early** — see below |
| Partners > Website Settings (fragments / banners / image bars / sport widgets / footer / casino / virtual / translations / styles) | `Pam.Cms` (heavy) | deferred |
| Partners > Backup & Restore | event-sourced state per module | covered by event sourcing approach |
| Bet Shops / Providers / Product Categories / Products | `Pam.GameCatalog` | deferred |
| Accounting (bet shop monthly) | `Pam.Reporting` (read-side projection) | deferred |
| Reports (Bets / Clients / Documents / Providers / Identity) | `Pam.Reporting` | deferred |
| Notifications (sent log) | `Pam.Notifications` | deferred |
| Currencies | reference data in `Pam.Partners` | deferred |
| CRM Templates | `Pam.Notifications` (templates) | deferred |
| CMS (Banners / Promotions / Regions / Comment Types / Job Areas / Translations / Enumerations / Security Questions / Online Shop) | `Pam.Cms` + `Pam.Shop` | deferred |
| (Implicit) Game wallet integration with providers | `Pam.GameWallet` (separate host) | deferred |

## Decisions to make NOW to enable "10x better"

These are the architectural calls that are cheap if made before scaling out
the modules and very expensive afterwards. They're load-bearing for the
whole product, not for any one module.

### 1. Multi-brand is a first-class aggregate, not a column

Core Platform's "Partner" is everywhere but feels retrofitted. We bake it
in from day one — under the term **Brand** (we're a single company running
multiple consumer-facing brands; "Partner" is a B2B term that doesn't fit):

- A `Brand` aggregate in `Pam.Operators` is the first non-Player concept
  (POC ships a hardcoded `IBrandRegistry` until the module lands).
- Every other aggregate carries a `BrandId` foreign reference.
- Brand maps to a ZITADEL Org. The same physical human registering on
  Brand A and Brand B is two distinct `Player` rows (per-brand records);
  cross-brand identity linking is a future optional `Subject` concept,
  introduced only when regulation demands it.
- A `IBrandContext` (resolved from the JWT's Org claim or, for anonymous
  registration, from the `X-Brand` header) drives runtime scoping.
- Each `DbContext` will gain a global query filter on `BrandId` so
  cross-brand reads are impossible by default once back-office endpoints
  arrive.
- Operator endpoints can elevate to cross-brand access via a specific
  policy (`operator.platformAdmin`).

Cost of doing this in v2: rewriting every query in every module. Cost of
doing it now: one filter per DbContext. Easy choice.

### 2. Wallet is double-entry from day one

Core Platform exposes "Used Balance / Unused Balance / Bonus Balance / Comp
Balance / Coin Balance" — five ad-hoc balances with no clear consistency
story. The "Corrections" section lets operators directly debit/credit any
balance.

We replace this with a **double-entry ledger**:

- One `LedgerEntry` aggregate (immutable, append-only).
- `Account` types: `cash`, `bonus`, `loyalty_comp`, `loyalty_coin`,
  `held_for_bet`, `held_for_withdrawal`, etc.
- A `Wallet` projection that aggregates entries into balances per account
  type. Snapshots every N entries for performance.
- "Corrections" become specific posting types in the ledger (with an
  operator actor and a reason code), not arbitrary balance writes.
- Outbox-published `LedgerPosted` integration events power Reporting,
  Audit, Notifications.

Reason: regulators require immutable transaction history. Ad-hoc
balance-mutation is the #1 cause of wallet incidents in iGaming. Modeling
it correctly once is dramatically cheaper than retrofitting.

### 3. Bonus engine: typed DSL, not enum-driven hardcoded conditions

Core Platform's bonus engine is a `BonusConditionTypes` enum (Sport,
Region, Match, Selection, Stake, etc.) crossed with an `OperationTypes`
enum (IsEqualTo, InSet, AtLeastOneInSet, ...). It's a proprietary
mini-DSL embedded in their database.

Better: use a **typed expression language** (e.g. CEL / NRules / a small
custom Specification pattern) that:

- Compiles to typed predicates against domain events.
- Evaluates in real time by subscribing to integration events
  (`BetPlaced`, `BetSettled`, `DepositCompleted`, `LoginCompleted`).
- Has a designer UI in the BO that produces the same expression tree.
- Can be unit-tested standalone.

Bonus types from the manual (Campaign Wager Sport/Casino, Cash, Free Bet,
Free Spin, Cashback, Affiliate, Raffle, Tournament, Challenges) become
specific configurations of the same engine, not separate code paths.

### 4. Audit is event-sourced and immutable, not "Changes History" notes

Core Platform has a "Changes History" panel on each entity showing who
edited what. That's app-level audit, fine for ops. **Regulatory** audit
needs to be append-only and tamper-evident.

`Pam.Audits` (event-sourced, no UPDATEs) subscribes to every integration
event and stores `(occurredAt, actor, ip, correlationId, payload, hash,
prevHash)`. Hashes chain so any tampering is detectable. Regulators ask;
this answers.

### 5. API-first with stable contracts

Core Platform is BO-first — the BO is the application. We invert: the API
is the application, and there are multiple consumers (BO UI, mobile app,
public website, partner integrations, game-wallet ingress).

Practically:

- Every module exposes its public surface as `Pam.<Module>.Contracts`
  (already established).
- OpenAPI spec is the source of truth, exposed via Scalar.
- Typed clients (NSwag-generated) for BO UI.
- Versioning from day one (`/v1/...`) so contract evolution is explicit.

### 6. Modern auth replaces "Force Block at 5 wrong passwords"

Core Platform's lockout is primitive: 5 wrong passwords → "Force Block"
that requires support intervention. Their state machine is
Active/Blocked/Disabled/Force Block/Suspended.

We have ZITADEL driving:

- Configurable brute-force protection per Org / instance policy.
- MFA (TOTP, WebAuthn, SMS).
- Session revocation tied to self-exclusion (`Limits` module emits
  `SelfExclusionActivated` → `Pam.Players` calls
  `IIdentityProvider.RevokeAllSessionsAsync`).
- Federation for operators (Azure AD / Google Workspace).
- Concurrent session limits, session listing, "log out everywhere."

Their five player states map onto our richer state machine + ZITADEL
session/credential state, with each transition emitting a domain event
that Audit consumes.

### 7. Real-time via streaming, not polling

Core Platform has "Online Clients (Real Time)" and a Dashboard with
metrics. Their UX implies polling.

We push:

- Integration events fan out via MassTransit.
- A SignalR (or native WebSocket) hub in `Pam.Api` subscribes to relevant
  events and pushes to BO clients.
- The BO Dashboard is a live projection of `LoginCompleted`,
  `DepositCompleted`, `BetSettled`, etc.

### 8. Typed config, not JSON-in-BO

Core Platform's "Web Fragments" / "Style Type" / "Banner Settings" all
have free-form JSON inputs in the BO. This is brittle (typos break the
site, no validation).

We define typed schemas per fragment kind (`BannerFragment`,
`ImageBarFragment`, `SportWidgetFragment`...) with versioning and
JSON-schema-driven validation in the API. The BO renders a typed form
per kind, not a free-text JSON box.

### 9. CMS is its own bounded context with snapshotting

Core Platform's website builder (Banners + Promotions + Web Fragments +
Translations + Styles + Image Bars + Sport Widgets + Footer per partner)
is genuinely a CMS. Treat it as one:

- `Pam.Cms` module with its own schema.
- Snapshots: a partner's entire website config can be cloned, branched,
  rolled back. (Their "Backup & Restore for Website Settings" feature
  becomes free with event sourcing.)
- Preview environment: a partner sees their changes on a staging domain
  before publishing.

### 10. Localization at the API layer

Core Platform stores translations per-partner per-section in the BO and
operators must manually upload. We do:

- Translatable resources are first-class with a `LocalizedText` value
  object (`{en: "...", es: "...", ...}`).
- Default language fallback chain.
- Translation management via Crowdin/Lokalise integration so non-technical
  staff can manage translations outside the BO.

## Build order, revised given the full feature surface

Ordered by what unblocks the most subsequent modules:

1. **Pam.Operators (Brand + Jurisdiction registry)** — even before Wallet.
   The POC ships a hardcoded `IBrandRegistry`; promote to a real module
   before module #2. Without this, every later module has to be
   retrofitted.
2. **Pam.Players KYC + Sessions + Limits/Exclusions** — extending the
   existing module. Pure additions, no boundary changes.
3. **Operator audience (ZITADEL Project) + admin endpoints** — now we can
   act on accounts. Unlocks back-office UI prototyping.
4. **Pam.Wallets (double-entry ledger)** — the most regulated module.
   Outbox is non-negotiable here.
5. **Pam.GameCatalog + Pam.GameWallet (separate host)** — enables real
   bets to flow.
6. **Pam.Bets + Pam.Bonuses (with the typed DSL design)** — depends on
   Wallet + GameCatalog.
7. **Pam.Notifications + Pam.Audits** — subscribing modules; cheap once
   integration events exist.
8. **Pam.Affiliates** — own admin panel, commissions, referral tracking.
9. **Pam.Cms + Pam.Shop** — biggest UI surface; can be built late.
10. **Pam.Reporting** — read-side projections over events; can be
    incrementally added.

The first-month-of-real-work focus is items 1-3 (Brands + KYC + Sessions
+ Limits + Operators) plus the back-office UI scaffold. Item 4 (Wallets)
is the next major milestone after that.
