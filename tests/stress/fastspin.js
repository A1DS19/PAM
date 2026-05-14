// k6 stress test — FastSpin (Kingdom Casino) intercept path.
//
// Goal: drive POST /v1/ingest/vendors/fastspin/main with unique transferId
// values so every request lands on the INSERT path. Measures the
// intercept end-to-end:
//   HTTP → endpoint → StubFastSpinUpstream (no outbound) → FastSpinAdapter
//   → IngestTransactionHandler → COMMIT #1 → COMMIT #2 →
//   BusOutboxDeliveryService → RabbitMQ → (discard consumer).
//
// Run the API in Stress mode first (see tests/stress/README.md or
// `make stress-api`). Stress mode flips Stress:FastSpinUpstreamStub:Enabled
// so the upstream forward is a no-op fake — without it every request
// would hit the real GBS dev endpoint, dominating the numbers.
//
// Usage:
//   k6 run --env VUS=50 --env DURATION=2m tests/stress/fastspin.js
//
// Required env (all optional, sensible defaults):
//   BASE_URL    default http://localhost:5000
//   VUS         peak virtual users, default 50
//   DURATION    steady-state duration, default 90s
//   ACCT_ID     player acctId echoed on the wire, default "stress-player"

import http from 'k6/http';
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const VUS = parseInt(__ENV.VUS || '50', 10);
const DURATION = __ENV.DURATION || '90s';
const ACCT_ID = __ENV.ACCT_ID || 'stress-player';

const accepted = new Counter('pam_accepted');
const rateLimited = new Counter('pam_rate_limited');
const upstreamErrors = new Counter('pam_upstream_errors');
const otherErrors = new Counter('pam_other_errors');
const ingestLatency = new Trend('pam_ingest_latency_ms', true);

export const options = {
  scenarios: {
    fresh_inserts: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: VUS },
        { duration: DURATION, target: VUS },
        { duration: '20s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    'http_req_duration{expected_response:true}': ['p(95)<200', 'p(99)<500'],
    'http_req_failed': ['rate<0.01'],
    'pam_other_errors': ['count<10'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(50)', 'p(95)', 'p(99)', 'max'],
};

// FastSpin transfer type discriminators (see FastSpinAdapter.MapTransferType):
//   1 = place bet (Risk, negative), 2 = cancel bet (Risk),
//   4 = payout (Win), 7 = bonus (Bonus). Other values are unmodeled and
//   the adapter skips persistence — keep them out of the mix.
const TRANSFER_TYPES = [1, 2, 4, 7];

function payload() {
  // transferId is the idempotency key (UNIQUE on (vendor_id, vendor_reference)).
  // High cardinality keeps every request on the INSERT path.
  const transferId = `stress-${__VU}-${__ITER}-${randomString(8)}`;
  const type = TRANSFER_TYPES[Math.floor(Math.random() * TRANSFER_TYPES.length)];
  // FastSpin sends amount as a positive double in currency units.
  // Random 1.00..500.00; the adapter applies sign based on type.
  const amount = (Math.floor(Math.random() * 49901) + 100) / 100;
  // transferTime is yyyyMMddTHHmmss in the vendor's clock — feed UTC now.
  const now = new Date();
  const transferTime =
    now.getUTCFullYear().toString().padStart(4, '0') +
    (now.getUTCMonth() + 1).toString().padStart(2, '0') +
    now.getUTCDate().toString().padStart(2, '0') +
    'T' +
    now.getUTCHours().toString().padStart(2, '0') +
    now.getUTCMinutes().toString().padStart(2, '0') +
    now.getUTCSeconds().toString().padStart(2, '0');
  return {
    transferId,
    acctId: ACCT_ID,
    currency: 'EUR',
    amount,
    type,
    channel: 'web',
    gameCode: 'S-STRESS',
    ticketId: `ticket-${__VU}-${__ITER}`,
    referenceId: `round-${__VU}-${Math.floor(__ITER / 5)}`,
    transferTime,
    merchantCode: 'FS',
    serialNo: randomString(12),
  };
}

const headers = {
  'Content-Type': 'application/json',
  // FastSpin's verb dispatch is via the API header. `transfer` is the
  // verb that persists a row; getBalance would be forward-only.
  'API': 'transfer',
  // PAM is intentionally transparent to the Digest header — GBS validates
  // it upstream. Stub upstream ignores the value, so any string works.
  'Digest': 'stress-fake-digest',
};

export default function () {
  const res = http.post(
    `${BASE_URL}/v1/ingest/vendors/fastspin/main`,
    JSON.stringify(payload()),
    {
      headers,
      tags: { name: 'POST /v1/ingest/vendors/fastspin/main' },
    }
  );

  ingestLatency.add(res.timings.duration);

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  if (res.status === 200) {
    // The endpoint relays upstream bytes verbatim. With the stub we get
    // a canned FastSpinTransferResponse with code=0 — count 200 as
    // accepted regardless of body shape (parsing it would add VU CPU
    // and the stress harness doesn't need it).
    accepted.add(1);
  } else if (res.status === 429) {
    rateLimited.add(1);
  } else if (res.status === 502 || res.status === 503) {
    // 502 = upstream unreachable, 503 = upstream timeout. With the stub
    // these should be 0 — non-zero means the stub isn't wired (check
    // Stress:FastSpinUpstreamStub:Enabled).
    upstreamErrors.add(1);
  } else {
    otherErrors.add(1);
  }
}

export function handleSummary(data) {
  return {
    stdout: textSummary(data),
    'stress-summary.json': JSON.stringify(data, null, 2),
  };
}

function textSummary(data) {
  const m = data.metrics;
  const pct = (n) => (n == null ? '—' : Math.round(n));
  const lines = [
    '',
    '== PAM FastSpin stress summary ==',
    `  iters:        ${pct(m.iterations?.values?.count)}`,
    `  duration:     ${pct(m.iteration_duration?.values?.avg)} ms avg`,
    `  HTTP p50:     ${pct(m.http_req_duration?.values?.['p(50)'])} ms`,
    `  HTTP p95:     ${pct(m.http_req_duration?.values?.['p(95)'])} ms`,
    `  HTTP p99:     ${pct(m.http_req_duration?.values?.['p(99)'])} ms`,
    `  RPS:          ${pct(m.http_reqs?.values?.rate)}`,
    `  accepted:     ${pct(m.pam_accepted?.values?.count)}`,
    `  rate-limited: ${pct(m.pam_rate_limited?.values?.count)}`,
    `  upstream:     ${pct(m.pam_upstream_errors?.values?.count)}`,
    `  errors:       ${pct(m.pam_other_errors?.values?.count)}`,
    '',
  ];
  return lines.join('\n');
}
