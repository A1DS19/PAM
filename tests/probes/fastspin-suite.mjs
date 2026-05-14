#!/usr/bin/env node
//
// FastSpin probe suite — runs a sequence of probes against the same
// account and asserts the cross-step invariant (balance moves by exactly
// the credited amount). Useful as a smoke test before declaring a QA
// environment healthy.
//
// Usage:
//   node tests/probes/fastspin-suite.mjs <acctId>
//   VIA_PAM=1 node tests/probes/fastspin-suite.mjs <acctId>
//
// Exits 0 on full pass, 1 if any step fails or the balance math
// disagrees. Real money moves on the upstream (bonus credit, type=7) —
// pick a bot account.

import { probe, reportProbe } from "./fastspin.mjs";

const acctId = process.argv[2];
if (!acctId) {
  console.error("usage: fastspin-suite.mjs <acctId>");
  process.exit(64);
}

const CREDIT_AMOUNT = Number.parseFloat(process.env.AMOUNT ?? "1.00");
const viaPam = process.env.VIA_PAM === "1";

console.log(
  `FastSpin probe suite — acctId=${acctId}, credit=$${CREDIT_AMOUNT}, viaPam=${viaPam}\n`,
);

let exitCode = 0;

// 1. Balance before
const before = await probe({ verb: "balance", acctId });
exitCode |= reportProbe(before, "balance·before");
if (!before.ok) process.exit(exitCode || 1);
const initial = before.parsed.acctInfo.balance;

// 2. Bonus credit (type=7)
const credit = await probe({
  verb: "transfer",
  acctId,
  amount: CREDIT_AMOUNT,
  type: 7,
});
exitCode |= reportProbe(credit, "transfer·bonus");
if (!credit.ok) process.exit(exitCode || 1);
const reportedAfter = credit.parsed.balance;

// 3. Balance after (independent read)
const after = await probe({ verb: "balance", acctId });
exitCode |= reportProbe(after, "balance·after");
if (!after.ok) process.exit(exitCode || 1);
const actualAfter = after.parsed.acctInfo.balance;

// 4. Cross-step assertion: balance moved by exactly CREDIT_AMOUNT.
// Float-money tolerance: round to 2 decimal places before comparing
// (GBS's wire is float-noisy, e.g. 10132.000004882813).
const round2 = (n) => Math.round(n * 100) / 100;
const expected = round2(initial + CREDIT_AMOUNT);
const reported = round2(reportedAfter);
const actual = round2(actualAfter);

console.log(
  `\nbalance movement: ${initial} → ${actualAfter}` +
    `  (transfer reported ${reportedAfter}, suite expected ${expected})`,
);

if (expected !== reported) {
  console.log(`✗ transfer's reported balance ≠ initial + credit`);
  exitCode |= 1;
}
if (expected !== actual) {
  console.log(`✗ second balance read disagrees with expected`);
  exitCode |= 1;
}

if (exitCode === 0) {
  console.log("\n✓ suite passed");
}
process.exit(exitCode ? 1 : 0);
