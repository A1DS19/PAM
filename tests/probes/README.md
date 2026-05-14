# Probes

Manual end-to-end probes against live upstreams. Run by hand when
validating an environment (QA bring-up, post-deploy sanity check,
diagnosing a "is GBS up?" question). Distinct from:

- `api/tests/` — automated .NET test projects (unit, integration,
  architecture) that run on every commit / push.
- `tests/stress/` — k6 load harness against synthetic upstreams.

Probes here hit **real upstreams** with **real (bot) money** and are
deliberately not part of CI.

## What's here

| File | Purpose |
|---|---|
| `fastspin.mjs` | Single FastSpin call (balance or transfer). Asserts response shape; if `VIA_PAM=1` + transfer, also verifies the `ingest.vendor_transactions` row matches the response. Exit 0 / 1. |
| `fastspin-suite.mjs` | Multi-step: balance → bonus credit → balance. Asserts the post-credit balance equals `initial + credit`. Exit 0 / 1. |

## Running

```bash
# Read-only balance probe — safe to run anywhere
node tests/probes/fastspin.mjs balance 0022

# Money-moving transfer — directly to GBS (no PAM in the loop)
node tests/probes/fastspin.mjs transfer 0022 1.00 7

# Same transfer routed through PAM, plus DB row verification
VIA_PAM=1 node tests/probes/fastspin.mjs transfer 0022 1.00 7

# Multi-step suite — the recommended environment smoke test
VIA_PAM=1 node tests/probes/fastspin-suite.mjs 0022
```

Transfer `type` legend: `1` = bet/debit (Risk), `2` = refund,
`4` = payout (Win), `7` = bonus credit.

## Configuration

Defaults match the dev environment. Override via env vars:

| Variable | Default | Notes |
|---|---|---|
| `GBS_URL` | `https://dev-gbs-api-dbdev.lucky99.eu/api/fastspin/main` | Where the upstream lives |
| `GBS_KEY` | `WETECHwsa3eYG2gfuTpFhN` | FastSpin `securityKey` for `MD5(body+key)` digest |
| `MERCHANT` | `WETECH` | `merchantCode` sent on every call |
| `PAM_URL` | `http://localhost:5000/v1/ingest/vendors/fastspin/main` | PAM intercept endpoint |
| `VIA_PAM` | unset | Set to `1` to route through PAM (and enable DB verification) |
| `PAM_DB_CONTAINER` | `pam-mssql` | Docker container running SQL Server |
| `PAM_DB`, `PAM_DB_USER`, `PAM_DB_PASSWORD` | match `docker-compose.yml` | sqlcmd connection for DB checks |

## Safety

These hit a live upstream. Things to keep in mind:

1. **Use bot accounts.** The customers in `gbs-dev` flagged
   `CustomerIsABot: true` are designated for testing — their balances
   are play money. Don't pick a non-bot account.
2. **Bonus (type=7) is the safest test type** — pure credit, no
   precondition on a prior bet, no real debit risk. Suite uses this.
3. **Bet (type=1) requires sufficient balance.** If the account is at
   zero balance and `CreditLimit` is exhausted, GBS will reject. Check
   the customer's `CurrentBalance` before debiting.
4. **Refund (type=2) requires a matching prior bet** via
   `refTicketIds`. The current probe doesn't model that — don't try
   `type=2` without extending the script.
5. **Don't run a tight loop.** Each iteration is a real upstream call
   that GBS logs. Suite runs three calls per acctId; that's fine. A
   thousand iterations would be noisy on a shared dev system. For
   throughput testing use `tests/stress/fastspin.js` with the stub.

## Adding a new probe

Follow the same pattern. Each probe should:

- Export a callable `probe()` (so the suite runner can compose it).
- Return a `{ ok, errors[], ... }` result — never throw on assertion
  failure; report and let the caller decide whether to continue.
- Provide a CLI shim that calls `process.exit(reportProbe(result))`.
- Keep the upstream secret out of the file — env var with a documented
  dev default is fine; production secrets never get committed.
