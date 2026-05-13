# Core Platform → PAM mapping

Feature-parity reference plus the "10x better" thesis. Source manual:
`~/Downloads/Core Platform.pdf` (68 pages, BO-user oriented).

## What Core Platform is

A multi-tenant iGaming back-office (sportsbook + casino + bet shops).
The system is **partner-scoped** — every entity belongs to a "Partner"
(the white-label brand). Partners own product catalog, currencies,
languages, payment providers, fragments, banners, translations, limits.

## Section → module mapping

| Core Platform section | PAM module | Status |
|---|---|---|
| Partners (Main / Payment / Product / Currency / Language / Provider / Limits / Country / Comp Points / Payment Info / Keys) | `Pam.Operators` (we use **Brand**, not Partner) | **v1 (Brand CRUD)** — Phase 2 shipped |
| Users & Agents + Roles | `Pam.Identity` (OpenIddict + ASP.NET Identity) | **Phase 3 — in flight** |
| Clients > Main / Notes / Settings | `Pam.Players` | deferred (module #3) |
| Clients > Sessions | `Pam.Players` (Sessions sub-aggregate) | deferred |
| Clients > KYC | `Pam.Kyc` (likely starts inside `Pam.Players`) | deferred |
| Clients > Limits & Exclusions / Product Limits | `Pam.Limits` | deferred |
| Clients > Corrections / Deposits / Withdrawals / Account History / Transactions / Payment Info / Payment Settings | `Pam.Wallet` | deferred (most regulated) |
| Clients > Campaigns / Bets | `Pam.Bonuses` + `Pam.Bets` | deferred |
| Clients > Tickets | `Pam.Support` | deferred |
| Clients > Friends | `Pam.Affiliates` (referrals) | deferred |
| Clients > Emails & SMSes / Provider Settings | `Pam.Notifications` + `Pam.Players` | deferred |
| Online Clients (Real Time) | streaming projection over `Pam.Players` sessions | deferred |
| Affiliates + admin panel | `Pam.Affiliates` (separate module + own auth) | deferred |
| Payments (global) | `Pam.Wallet` query side | deferred |
| Bets / Internet Bets / Bet Shops | `Pam.Bets` + `Pam.BetShops` | deferred |
| Bonuses (Common + Triggers + Conditions DSL) | `Pam.Bonuses` | deferred (own design pass) |
| Segments | `Pam.Segments` (cross-cutting projection) | deferred |
| Partners > Website Settings (fragments / banners / image bars / sport widgets / footer / casino / virtual / translations / styles) | `Pam.Cms` | deferred |
| Partners > Backup & Restore | event-sourced state per module | covered by event sourcing |
| Bet Shops / Providers / Product Categories / Products | `Pam.GameCatalog` | deferred |
| Accounting (bet shop monthly) | `Pam.Reporting` (read-side projection) | deferred |
| Reports (Bets / Clients / Documents / Providers / Identity) | `Pam.Reporting` | deferred |
| Notifications (sent log) | `Pam.Notifications` | deferred |
| Currencies | reference data in `Pam.Operators` | deferred |
| CRM Templates | `Pam.Notifications` (templates) | deferred |
| CMS (Banners / Promotions / Regions / Comment Types / Job Areas / Translations / Enumerations / Security Questions / Online Shop) | `Pam.Cms` + `Pam.Shop` | deferred |
| (Implicit) Game wallet integration | `Pam.GameWallet` (separate host) | deferred |

## "10x better" — load-bearing decisions

These are architectural calls that are cheap before scaling out modules
and very expensive after.

### 1. Multi-brand is a first-class aggregate

Core Platform's "Partner" feels retrofitted. We bake it in as **Brand**
from day one.

- `Brand` aggregate in `Pam.Operators` — first module, shipped Phase 2.
- Every other aggregate carries `BrandId`.
- Same physical human registering on Brand A and Brand B = two distinct
  `Player` rows. Cross-brand identity linking is a future `Subject`
  concept, only when regulation demands it.
- Back-office actors carry `brand_id` JWT claim from the embedded IDP;
  anonymous flows take brand from a header.
- Each `DbContext` gets a global query filter on `BrandId` — cross-brand
  reads impossible by default. Cross-brand access elevates via
  `operator.platform-admin`.

**Cost in v2:** rewrite every query. **Cost now:** one filter per DbContext.

### 2. Wallet is double-entry from day one

Core Platform exposes 5 ad-hoc balances (Used / Unused / Bonus / Comp /
Coin) with "Corrections" that directly mutate them. No consistency story.

We use a **double-entry ledger**:

- `LedgerEntry` aggregate, immutable, append-only.
- Account types: `cash`, `bonus`, `loyalty_comp`, `loyalty_coin`,
  `held_for_bet`, `held_for_withdrawal`, etc.
- `Wallet` projection aggregates entries into balances. Snapshots every
  N entries for performance.
- "Corrections" = specific posting types with operator actor + reason
  code, not arbitrary balance writes.
- `LedgerPosted` integration events power Reporting, Audit, Notifications.

**Why:** regulators require immutable transaction history. Ad-hoc
balance-mutation is the #1 cause of wallet incidents in iGaming.

### 3. Bonus engine: typed DSL

Core Platform's bonus engine is a `BonusConditionTypes` enum crossed
with `OperationTypes` — a proprietary mini-DSL in their database.

Better: typed expression language (CEL / NRules / a small Specification
pattern):

- Compiles to typed predicates against domain events.
- Evaluates in real time by subscribing to integration events
  (`BetPlaced`, `BetSettled`, `DepositCompleted`, `LoginCompleted`).
- BO designer UI produces the same expression tree.
- Unit-testable standalone.

The manual's bonus types (Cash, Free Bet, Free Spin, Cashback, Affiliate,
Raffle, Tournament, Challenges) become configurations of one engine, not
separate code paths.

### 4. Audit is event-sourced and immutable

Core Platform's "Changes History" is app-level audit — fine for ops.
Regulatory audit needs to be append-only and tamper-evident.

`Pam.Audits` (event-sourced, no UPDATEs) subscribes to every integration
event, stores `(occurredAt, actor, ip, correlationId, payload, hash,
prevHash)`. Hash chain detects tampering.

### 5. API-first with stable contracts

Core Platform is BO-first — the BO is the application. We invert: API
is the application, multiple consumers (BO UI, mobile, public website,
partner integrations, game-wallet ingress).

- `Pam.<Module>.Contracts` per module — already established.
- OpenAPI is the source of truth, exposed via Scalar.
- NSwag-generated typed clients for BO UI.
- `/v1/...` versioning from day one.

### 6. Modern auth replaces "Force Block at 5 wrong passwords"

Core Platform's lockout is primitive (Active / Blocked / Disabled /
Force Block / Suspended). We have **OpenIddict + ASP.NET Core Identity**
driving:

- Configurable brute-force (Identity lockout: 5 attempts → 5 min, tunable).
- MFA (TOTP today; WebAuthn via Fido2 package when triggered).
- Refresh-token rotation with replay detection.
- Token revocation on self-exclusion: `Limits` emits
  `SelfExclusionActivated` → `Pam.Identity` revokes via
  `EnableAuthorizationEntryValidation()`.
- Federation for operators (Azure AD, Google Workspace) when needed.
- Concurrent session limits, session listing, "log out everywhere."

### 7. Real-time via streaming

Core Platform "Online Clients (Real Time)" implies polling. We push:

- Integration events fan out via MassTransit.
- SignalR / WebSocket hub in `Pam.Api` subscribes to relevant events.
- BO Dashboard is a live projection of `LoginCompleted`,
  `DepositCompleted`, `BetSettled`, etc.

### 8. Typed config, not JSON-in-BO

Core Platform's "Web Fragments" / "Style Type" / "Banner Settings"
accept free-form JSON in the BO. Brittle, no validation.

We define typed schemas per fragment kind (`BannerFragment`,
`ImageBarFragment`, `SportWidgetFragment`...) with versioning and
JSON-schema validation. BO renders a typed form per kind.

### 9. CMS as its own bounded context with snapshots

Core Platform's website builder is genuinely a CMS. Treat it as one:

- `Pam.Cms` module with own schema.
- Snapshots: clone, branch, roll back a partner's website config — their
  "Backup & Restore" feature is free with event sourcing.
- Preview environment: staging domain before publishing.

### 10. Localization at the API layer

Core Platform stores translations per-partner per-section in the BO;
operators manually upload. We use:

- First-class `LocalizedText` value object (`{en: "...", es: "...", ...}`).
- Default language fallback chain.
- Crowdin/Lokalise integration so non-technical staff manage translations
  outside the BO.

## Build order

Ordered by what unblocks the most downstream:

| # | Module | Status | Why this slot |
|---|---|---|---|
| 1 | `Pam.Operators` (Brand) | **shipped Phase 2** | Multi-tenant foundation |
| 2 | `Pam.Identity` | **Phase 3, in flight** | No human can act on anything without it |
| 3 | `Pam.Players` | next | Scoped under Brand, authenticated via Identity |
| 4 | `Pam.Wallet` (double-entry) | after #3 | Most regulated; outbox is non-negotiable |
| 5 | `Pam.GameProviders` / `Pam.GameCatalog` | after #4 | Enables real bets |
| 6 | `Pam.GameWallet` (separate host) | after #5 | HMAC-signed game ingress, sub-200ms p99 |
| 7 | `Pam.Bets` + `Pam.Bonuses` (typed DSL) | after #6 | Depends on Wallet + GameCatalog |
| 8 | `Pam.Notifications` + `Pam.Audits` | parallel | Subscribing; cheap once events exist |
| 9 | `Pam.Affiliates` | after #4 | Own admin panel + commissions |
| 10 | `Pam.Cms` + `Pam.Shop` | late | Biggest UI surface |
| 11 | `Pam.Reporting` | incremental | Read-side projections over events |

Current focus: items 2–3 (Identity + Players). Item 4 (Wallet) is the
next major milestone after that.
