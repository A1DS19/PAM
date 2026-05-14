#!/usr/bin/env node
//
// FastSpin batch probe — fires a mix of requests against existing and
// non-existing acctIds, across multiple transfer types, to validate:
//
//   1. PAM forwards every call to GBS (no errors except UpstreamUnreachable).
//   2. Existing acctIds land code=0 with full response shape.
//   3. Missing acctIds land code!=0 (vendor rejection); response still
//      relayed and a row STILL persists — that's the audit story.
//   4. The row delta in ingest.vendor_transactions equals the call count
//      regardless of code, since FastSpinInterceptEndpoint persists for
//      every Forwarded transfer.
//
// Usage:
//   VIA_PAM=1 node tests/probes/fastspin-batch.mjs
//
// Env:
//   COUNT          default 100 — number of calls
//   AMOUNT         pins the per-call dollar amount; if unset, each call
//                  picks a random amount in [AMOUNT_MIN, AMOUNT_MAX]
//                  (cents-resolution) to exercise float-money rounding
//   AMOUNT_MIN     default 0.50
//   AMOUNT_MAX     default 50.00
//   EXISTING       comma-separated acctIds known to exist (default: the
//                  12 acctIds 12798..12814 from gbs-dev)
//   MISSING        comma-separated acctIds known NOT to exist (default:
//                  50 synthesized IDs 99000..99049)
//   VIA_PAM=1      route through PAM intercept; enables DB row-count check
//   SEED           RNG seed for reproducible plan (optional)

import { probe } from "./fastspin.mjs";
import { spawnSync } from "node:child_process";

const COUNT = Number.parseInt(process.env.COUNT ?? "100", 10);
const FIXED_AMOUNT = process.env.AMOUNT
  ? Number.parseFloat(process.env.AMOUNT)
  : null;
const AMOUNT_MIN = Number.parseFloat(process.env.AMOUNT_MIN ?? "0.50");
const AMOUNT_MAX = Number.parseFloat(process.env.AMOUNT_MAX ?? "50.00");
const VIA_PAM = process.env.VIA_PAM === "1";

const EXISTING_DEFAULT = [
  "12798", "12799", "12801", "12802", "12803", "12805",
  "12807", "12808", "12811", "12812", "12813", "12814",
];
const MISSING_DEFAULT = Array.from({ length: 50 }, (_, i) =>
  String(99000 + i).padStart(5, "0"),
);
const TYPES = [1, 4, 7]; // Risk, Win, Bonus — skip 2 (refund needs refTicketIds)

const EXISTING = (process.env.EXISTING ?? EXISTING_DEFAULT.join(",")).split(",");
const MISSING = (process.env.MISSING ?? MISSING_DEFAULT.join(",")).split(",");

// Mulberry32 PRNG for reproducible plans when SEED is set.
function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rand = process.env.SEED ? rng(Number.parseInt(process.env.SEED, 10)) : Math.random;
const pick = (arr) => arr[Math.floor(rand() * arr.length)];

function randomAmount() {
  if (FIXED_AMOUNT != null) return FIXED_AMOUNT;
  // Cents resolution so the wire-side value is realistic (no
  // floating-point silliness from the probe side; lets us isolate any
  // float-money artifacts to GBS).
  const cents =
    Math.floor(rand() * (AMOUNT_MAX * 100 - AMOUNT_MIN * 100 + 1)) +
    Math.floor(AMOUNT_MIN * 100);
  return cents / 100;
}

function buildPlan(count) {
  const plan = [];
  for (let i = 0; i < count; i++) {
    const isExisting = rand() < 0.5;
    plan.push({
      acctId: pick(isExisting ? EXISTING : MISSING),
      type: TYPES[Math.floor(rand() * TYPES.length)],
      amount: randomAmount(),
      isExisting,
    });
  }
  return plan;
}

function countFastSpinRows() {
  const r = spawnSync(
    "docker",
    [
      "exec", "-i", "pam-mssql",
      "/opt/mssql-tools18/bin/sqlcmd",
      "-S", "localhost", "-U", "sa", "-P", "Pam_dev_password_123!",
      "-No", "-I", "-b", "-d", "pam",
      "-h", "-1", "-W",
      "-Q", "SET NOCOUNT ON; SELECT COUNT(*) FROM ingest.vendor_transactions WHERE vendor_id='fastspin';",
    ],
    { encoding: "utf8" },
  );
  if (r.status !== 0) {
    console.error(`sqlcmd failed: ${r.stderr || r.stdout}`);
    return null;
  }
  return Number.parseInt(r.stdout.trim(), 10);
}

function percentile(sorted, p) {
  if (sorted.length === 0) return 0;
  const idx = Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length));
  return sorted[idx];
}

const amountDesc =
  FIXED_AMOUNT != null
    ? `$${FIXED_AMOUNT.toFixed(2)}`
    : `random $${AMOUNT_MIN.toFixed(2)}–$${AMOUNT_MAX.toFixed(2)}`;
console.log(
  `FastSpin batch — count=${COUNT}, amount=${amountDesc}, viaPam=${VIA_PAM}` +
    `, existing=${EXISTING.length}, missing=${MISSING.length}`,
);
if (process.env.SEED) console.log(`  seed=${process.env.SEED}`);
console.log("");

const plan = buildPlan(COUNT);
const beforeRows = VIA_PAM ? countFastSpinRows() : null;
if (VIA_PAM && beforeRows == null) {
  console.error("✗ couldn't snapshot DB row count — is mssql container up?");
  process.exit(1);
}

const results = [];
const t0 = performance.now();
for (let i = 0; i < plan.length; i++) {
  const step = plan[i];
  const r = await probe({
    verb: "transfer",
    acctId: step.acctId,
    amount: step.amount,
    type: step.type,
    skipDbCheck: true,
  });
  const code = r.parsed?.code;
  const expectedOk = step.isExisting ? code === 0 : code != null && code !== 0;
  results.push({
    ...step,
    code,
    msg: r.parsed?.msg,
    latencyMs: r.latencyMs,
    expectedOk,
    balance: r.parsed?.balance ?? null,
  });

  const tag = expectedOk ? "✓" : "✗";
  const cat = step.isExisting ? "EXIST" : "MISS ";
  const codeStr = code == null ? "?" : String(code);
  process.stdout.write(
    `[${(i + 1).toString().padStart(3)}/${COUNT}] ${tag} ${cat} ${step.acctId} ` +
      `type=${step.type} amt=$${step.amount.toFixed(2).padStart(6)} ` +
      `code=${codeStr.padStart(5)} ${r.latencyMs.toString().padStart(4)}ms\n`,
  );
}
const totalMs = Math.round(performance.now() - t0);
const afterRows = VIA_PAM ? countFastSpinRows() : null;

// Summary
console.log(`\n== Summary (total ${totalMs}ms) ==`);

const existing = results.filter((r) => r.isExisting);
const missing = results.filter((r) => !r.isExisting);
const allOk = results.every((r) => r.expectedOk);

console.log(
  `Existing acctIds: ${existing.length}, all code=0? ${existing.every((r) => r.code === 0) ? "✓" : "✗"}`,
);
console.log(
  `Missing  acctIds: ${missing.length}, all code≠0? ${missing.every((r) => r.code != null && r.code !== 0) ? "✓" : "✗"}`,
);

// Code distribution on missing
const missingCodes = {};
for (const r of missing) {
  const key = `code=${r.code} (${r.msg ?? ""})`;
  missingCodes[key] = (missingCodes[key] || 0) + 1;
}
console.log("\nMissing-acct response codes:");
for (const [k, v] of Object.entries(missingCodes)) {
  console.log(`  ${v.toString().padStart(3)}  ${k}`);
}

// Per type
console.log("\nBy transfer type:");
for (const t of TYPES) {
  const ofType = results.filter((r) => r.type === t);
  const exist = ofType.filter((r) => r.isExisting);
  const miss = ofType.filter((r) => !r.isExisting);
  console.log(
    `  type=${t}: ${ofType.length} calls — existing ${exist.length} (ok=${exist.filter((r) => r.code === 0).length}), missing ${miss.length} (rejected=${miss.filter((r) => r.code !== 0).length})`,
  );
}

// Latency stats
const sorted = results.map((r) => r.latencyMs).sort((a, b) => a - b);
console.log(
  `\nLatency:  min=${sorted[0]}  p50=${percentile(sorted, 50)}  p95=${percentile(sorted, 95)}  p99=${percentile(sorted, 99)}  max=${sorted[sorted.length - 1]} ms`,
);

// DB row delta + rejection-balance-null assertion (validates Fix #1
// from 2026-05-14: rejection rows should have vendor_balance_after_cents
// NULL, not 0).
let dbAssertionsOk = true;
if (VIA_PAM) {
  const delta = afterRows - beforeRows;
  const expected = COUNT;
  const dbOk = delta === expected;
  console.log(
    `\nDB rows: ${beforeRows} → ${afterRows}  delta=${delta}  expected=${expected}  ${dbOk ? "✓" : "✗"}`,
  );
  if (!dbOk) {
    console.log(
      `  ✗ ${expected - delta} call(s) didn't persist — check FastSpinInterceptEndpoint logs`,
    );
    dbAssertionsOk = false;
  }

  // Rejection rows must show vendor_balance_after_cents = NULL (not 0).
  const rejectionCheck = spawnSync(
    "docker",
    [
      "exec", "-i", "pam-mssql",
      "/opt/mssql-tools18/bin/sqlcmd",
      "-S", "localhost", "-U", "sa", "-P", "Pam_dev_password_123!",
      "-No", "-I", "-b", "-d", "pam",
      "-h", "-1", "-W",
      "-Q",
      "SET NOCOUNT ON; SELECT COUNT(*) FROM ingest.vendor_transactions " +
        "WHERE vendor_id='fastspin' AND downstream_outcome_code <> 0 " +
        "AND vendor_balance_after_cents IS NOT NULL;",
    ],
    { encoding: "utf8" },
  );
  const lyingRows = Number.parseInt(rejectionCheck.stdout.trim(), 10);
  if (Number.isFinite(lyingRows)) {
    if (lyingRows === 0) {
      console.log("Rejection-balance-null assertion: ✓ no rejection row carries a fake balance");
    } else {
      console.log(`Rejection-balance-null assertion: ✗ ${lyingRows} rejection row(s) have a non-NULL vendor_balance_after_cents`);
      dbAssertionsOk = false;
    }
  }
}

const overallOk = allOk && dbAssertionsOk;
console.log(`\nOverall: ${overallOk ? "✓ pass" : "✗ fail"}`);
process.exit(overallOk ? 0 : 1);
