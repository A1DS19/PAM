#!/usr/bin/env node
//
// FastSpin probe — manual end-to-end test against a real GBS instance,
// optionally via the PAM intercept. Exports a callable `probe()` for the
// suite runner; ships a CLI for one-off use.
//
// Usage (CLI):
//   node tests/probes/fastspin.mjs balance <acctId>
//   node tests/probes/fastspin.mjs transfer <acctId> <amount> <type>
//     type: 1=bet/debit, 2=refund, 4=payout, 7=bonus
//
// Examples:
//   node tests/probes/fastspin.mjs balance 0022
//   VIA_PAM=1 node tests/probes/fastspin.mjs transfer 0022 1.00 7
//
// Env:
//   GBS_URL       default https://dev-gbs-api-dbdev.lucky99.eu/api/fastspin/main
//   GBS_KEY       FastSpin securityKey (dev default baked in — change for prod)
//   MERCHANT      default WETECH
//   PAM_URL       default http://localhost:5000/v1/ingest/vendors/fastspin/main
//   VIA_PAM=1     route through PAM intercept; also enables post-call DB
//                 verification when verb=transfer
//   PAM_DB_*      sqlcmd container/credentials (see runDbCheck below for
//                 defaults — matches docker-compose.yml)

import { createHash, randomUUID } from "node:crypto";
import { spawnSync } from "node:child_process";

const DEFAULTS = {
  gbsUrl:
    process.env.GBS_URL ??
    "https://dev-gbs-api-dbdev.lucky99.eu/api/fastspin/main",
  pamUrl:
    process.env.PAM_URL ??
    "http://localhost:5000/v1/ingest/vendors/fastspin/main",
  gbsKey: process.env.GBS_KEY ?? "WETECHwsa3eYG2gfuTpFhN",
  merchant: process.env.MERCHANT ?? "WETECH",
  pamContainer: process.env.PAM_DB_CONTAINER ?? "pam-mssql",
  pamDb: process.env.PAM_DB ?? "pam",
  pamUser: process.env.PAM_DB_USER ?? "sa",
  pamPassword: process.env.PAM_DB_PASSWORD ?? "Pam_dev_password_123!",
};

const TRANSFER_KIND = { 1: "Risk", 2: "Risk", 4: "Win", 7: "Bonus" };

function md5(s) {
  return createHash("md5").update(s, "utf8").digest("hex");
}

function transferTimeNow() {
  const d = new Date();
  const pad = (n) => n.toString().padStart(2, "0");
  return (
    `${d.getUTCFullYear()}${pad(d.getUTCMonth() + 1)}${pad(d.getUTCDate())}` +
    `T${pad(d.getUTCHours())}${pad(d.getUTCMinutes())}${pad(d.getUTCSeconds())}`
  );
}

function buildBody({ verb, acctId, amount, type, merchant }) {
  const serialNo = randomUUID().replace(/-/g, "").slice(0, 16);
  if (verb === "balance") {
    return { acctId, gameCode: "S-RM01", merchantCode: merchant, serialNo };
  }
  return {
    transferId: `probe-${Date.now()}-${randomUUID().slice(0, 8)}`,
    acctId,
    currency: "USD",
    amount,
    type,
    channel: "web",
    gameCode: "S-RM01",
    ticketId: `tkt-${Date.now()}`,
    referenceId: `ref-${Date.now()}`,
    transferTime: transferTimeNow(),
    merchantCode: merchant,
    serialNo,
  };
}

// Validates a captured `ingest.vendor_transactions` row against the
// response PAM relayed. Only meaningful when going through PAM with a
// transfer verb that landed Forwarded — the persist path runs only then.
function runDbCheck({ transferId, expected, opts }) {
  const query = `
SET NOCOUNT ON;
SELECT
  vendor_id, vendor_reference, kind, amount_cents, currency,
  downstream_status, downstream_reference, downstream_outcome_code,
  downstream_outcome_message, vendor_balance_after_cents
FROM ingest.vendor_transactions
WHERE vendor_reference = '${transferId}';
`;
  const result = spawnSync(
    "docker",
    [
      "exec",
      "-i",
      opts.pamContainer,
      "/opt/mssql-tools18/bin/sqlcmd",
      "-S",
      "localhost",
      "-U",
      opts.pamUser,
      "-P",
      opts.pamPassword,
      "-No",
      "-I",
      "-b",
      "-d",
      opts.pamDb,
      "-h",
      "-1",
      "-W",
      "-s",
      "|",
      "-Q",
      query,
    ],
    { encoding: "utf8" },
  );
  if (result.status !== 0) {
    return {
      ok: false,
      errors: [`sqlcmd exited ${result.status}: ${result.stderr || result.stdout}`],
    };
  }
  const line = result.stdout
    .split("\n")
    .map((s) => s.trim())
    .find((s) => s && !s.startsWith("("));
  if (!line) {
    return { ok: false, errors: ["row not found in ingest.vendor_transactions"] };
  }
  const cols = line.split("|");
  const row = {
    vendor_id: cols[0],
    vendor_reference: cols[1],
    kind: cols[2],
    amount_cents: Number(cols[3]),
    currency: cols[4],
    downstream_status: cols[5],
    downstream_reference: cols[6],
    downstream_outcome_code: Number(cols[7]),
    downstream_outcome_message: cols[8],
    vendor_balance_after_cents: Number(cols[9]),
  };
  const errors = [];
  const check = (field, want) => {
    if (row[field] !== want) {
      errors.push(
        `row.${field}: expected ${JSON.stringify(want)}, got ${JSON.stringify(row[field])}`,
      );
    }
  };
  check("vendor_id", "fastspin");
  check("vendor_reference", transferId);
  check("kind", expected.kind);
  check("amount_cents", expected.amountCents);
  check("currency", "USD");
  check("downstream_status", "Forwarded");
  check("downstream_reference", expected.merchantTxId);
  check("downstream_outcome_code", 0);
  check("downstream_outcome_message", "Success");
  check("vendor_balance_after_cents", expected.balanceCents);
  return { ok: errors.length === 0, errors, row };
}

// Sends one FastSpin request, validates the response shape, and (when
// going via PAM with a transfer) verifies the persisted row. Returns
// `{ ok, errors[], request, response, parsed, latencyMs, viaPam, dbRow? }`.
export async function probe({
  verb,
  acctId,
  amount,
  type,
  viaPam = process.env.VIA_PAM === "1",
  skipDbCheck = false,
  opts = DEFAULTS,
}) {
  if (verb !== "balance" && verb !== "transfer") {
    throw new Error(`verb must be 'balance' or 'transfer'`);
  }
  if (verb === "transfer" && (amount == null || ![1, 2, 4, 7].includes(type))) {
    throw new Error("transfer requires amount + type in {1,2,4,7}");
  }

  const body = buildBody({ verb, acctId, amount, type, merchant: opts.merchant });
  const json = JSON.stringify(body);
  const digest = md5(json + opts.gbsKey);
  const url = viaPam ? opts.pamUrl : opts.gbsUrl;
  const apiHeader = verb === "balance" ? "getBalance" : "transfer";

  const t0 = performance.now();
  const res = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "API": apiHeader,
      "Digest": digest,
    },
    body: json,
  });
  const text = await res.text();
  const latencyMs = Math.round(performance.now() - t0);

  const errors = [];
  if (res.status !== 200) errors.push(`HTTP ${res.status} (expected 200)`);

  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch {
    errors.push("response body is not JSON");
  }

  if (parsed) {
    if (parsed.code !== 0) errors.push(`response.code = ${parsed.code} (expected 0)`);
    if (verb === "transfer") {
      if (!parsed.merchantTxId) errors.push("response.merchantTxId missing");
      if (parsed.transferId !== body.transferId) {
        errors.push(
          `response.transferId echo mismatch (sent ${body.transferId}, got ${parsed.transferId})`,
        );
      }
      if (typeof parsed.balance !== "number") errors.push("response.balance not numeric");
    }
    if (verb === "balance") {
      if (!parsed.acctInfo) errors.push("response.acctInfo missing");
      else if (typeof parsed.acctInfo.balance !== "number") {
        errors.push("response.acctInfo.balance not numeric");
      }
    }
  }

  const result = {
    ok: errors.length === 0,
    errors,
    request: body,
    response: { status: res.status, body: text },
    parsed,
    latencyMs,
    viaPam,
  };

  // DB verification only makes sense for a successful transfer through
  // PAM. Skippable for batch callers that do aggregate validation.
  if (!skipDbCheck && viaPam && verb === "transfer" && result.ok) {
    // Persistence happens BEFORE relay, so the row exists by the time
    // we get the response. A tiny grace lets the SaveChanges commit be
    // visible to a fresh sqlcmd connection on a busy box.
    await new Promise((r) => setTimeout(r, 50));
    const expected = {
      kind: TRANSFER_KIND[type],
      amountCents:
        TRANSFER_KIND[type] === "Risk"
          ? -Math.round(amount * 100)
          : Math.round(amount * 100),
      merchantTxId: parsed.merchantTxId,
      balanceCents: Math.round(parsed.balance * 100),
    };
    const db = runDbCheck({ transferId: body.transferId, expected, opts });
    result.dbRow = db.row;
    if (!db.ok) {
      result.ok = false;
      result.errors.push(...db.errors.map((e) => `db: ${e}`));
    }
  }

  return result;
}

// Pretty-print a probe result. Returns the exit code (0 ok, 1 fail).
export function reportProbe(result, label = "") {
  const banner = label ? `[${label}] ` : "";
  if (result.ok) {
    const verbLabel = result.parsed?.transferId ? "transfer" : "balance";
    const balanceStr =
      result.parsed?.balance != null
        ? `  balance=${result.parsed.balance}`
        : result.parsed?.acctInfo?.balance != null
          ? `  balance=${result.parsed.acctInfo.balance}`
          : "";
    const tx = result.parsed?.merchantTxId
      ? `  merchantTxId=${result.parsed.merchantTxId}`
      : "";
    console.log(
      `${banner}✓ ${result.viaPam ? "via PAM " : ""}${verbLabel} ${result.latencyMs}ms${tx}${balanceStr}`,
    );
    if (result.dbRow) {
      console.log(
        `  db: row written — kind=${result.dbRow.kind} amount_cents=${result.dbRow.amount_cents} ` +
          `downstream_reference=${result.dbRow.downstream_reference} ` +
          `vendor_balance_after_cents=${result.dbRow.vendor_balance_after_cents}`,
      );
    }
    return 0;
  }
  console.log(`${banner}✗ failed (${result.latencyMs}ms)`);
  for (const e of result.errors) console.log(`    - ${e}`);
  if (result.response?.body) {
    console.log(`  raw body: ${result.response.body.slice(0, 400)}`);
  }
  return 1;
}

// CLI entrypoint.
if (import.meta.url === `file://${process.argv[1]}`) {
  const [verb, acctId, amountStr, typeStr] = process.argv.slice(2);
  if (!verb || !acctId) {
    console.error(
      "usage: fastspin.mjs <balance|transfer> <acctId> [amount] [type]",
    );
    process.exit(64);
  }
  const result = await probe({
    verb,
    acctId,
    amount: amountStr ? Number.parseFloat(amountStr) : undefined,
    type: typeStr ? Number.parseInt(typeStr, 10) : undefined,
  });
  process.exit(reportProbe(result));
}
