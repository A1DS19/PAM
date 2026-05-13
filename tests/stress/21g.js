// k6 stress test — 21G fresh-insert path.
//
// Goal: drive POST /v1/ingest/vendors/21g with unique Reference values so
// every request lands on the INSERT path (not the duplicate-violation
// fast path). Measures the publish side end-to-end:
//   HTTP → adapter → IngestTransactionHandler → COMMIT #1 → COMMIT #2 →
//   BusOutboxDeliveryService → RabbitMQ → (discard consumer).
//
// Run the API in Stress mode first (see tests/stress/README.md or
// `make stress-api`) so the rate limiter is disabled and the discard
// consumers are bound.
//
// Usage:
//   k6 run --env VUS=50 --env DURATION=2m tests/stress/21g.js
//
// Required env (all optional, sensible defaults):
//   BASE_URL    default http://localhost:5000
//   VUS         peak virtual users, default 50
//   DURATION    steady-state duration, default 90s
//   BRAND_ID    GUID, default "00000000-0000-0000-0000-000000000001"
//   PLAYER_ID   GUID, default "00000000-0000-0000-0000-000000000002"

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const VUS = parseInt(__ENV.VUS || '50', 10);
const DURATION = __ENV.DURATION || '90s';
const BRAND_ID = __ENV.BRAND_ID || '00000000-0000-0000-0000-000000000001';
const PLAYER_ID = __ENV.PLAYER_ID || '00000000-0000-0000-0000-000000000002';

const accepted = new Counter('pam_accepted');
const duplicates = new Counter('pam_duplicates');
const rateLimited = new Counter('pam_rate_limited');
const validationFails = new Counter('pam_validation_fails');
const otherErrors = new Counter('pam_other_errors');
const ingestLatency = new Trend('pam_ingest_latency_ms', true);

export const options = {
  scenarios: {
    fresh_inserts: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: VUS },     // ramp up
        { duration: DURATION, target: VUS },  // steady state
        { duration: '20s', target: 0 },       // ramp down
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: {
    // Indicative production gates — tighten on real hardware.
    'http_req_duration{expected_response:true}': ['p(95)<200', 'p(99)<500'],
    'http_req_failed': ['rate<0.01'],
    'pam_other_errors': ['count<10'],
  },
  // k6 only computes p(95) by default — opt into the rest so the summary
  // shows them. avg/min/med/max are k6 defaults; p(50) p(95) p(99) are
  // what the stress dashboard cares about.
  summaryTrendStats: ['avg', 'min', 'med', 'p(50)', 'p(95)', 'p(99)', 'max'],
  // OTel output via `k6 run --out experimental-opentelemetry` when wired,
  // or `--out json=summary.json` for offline analysis.
};

const KINDS = ['Risk', 'Win', 'Refund', 'Bonus', 'Correction'];

function payload() {
  // Unique Reference per request — avoids the duplicate-violation path.
  // 21G's real refs are vendor-shaped; for stress the shape is irrelevant
  // as long as cardinality is high.
  const ref = `stress-${__VU}-${__ITER}-${randomString(6)}`;
  const kind = KINDS[Math.floor(Math.random() * KINDS.length)];
  // Random amount 100..50000 cents = €1..€500. Sign is applied by the
  // adapter based on kind (Risk → negative). Send positive here per
  // the 21G convention documented on the endpoint.
  const amount = Math.floor(Math.random() * 49901) + 100;
  return {
    brandId: BRAND_ID,
    playerId: PLAYER_ID,
    reference: ref,
    amountCents: amount,
    currency: 'EUR',
    kind,
    occurredAt: new Date().toISOString(),
    roundId: `round-${__VU}-${Math.floor(__ITER / 5)}`,
    description: null,
  };
}

const headers = {
  'Content-Type': 'application/json',
  'X-Vendor-System-Id': 'stress-test',
};

export default function () {
  const res = http.post(`${BASE_URL}/v1/ingest/vendors/21g`, JSON.stringify(payload()), {
    headers,
    tags: { name: 'POST /v1/ingest/vendors/21g' },
  });

  ingestLatency.add(res.timings.duration);

  check(res, {
    'status is 200': (r) => r.status === 200,
  });

  if (res.status === 200) {
    // Body shape: { transactionId, status, rejectedReason }
    try {
      const body = res.json();
      if (body.status === 'Received') accepted.add(1);
      else if (body.status === 'Duplicate') duplicates.add(1);
      else if (body.status === 'Rejected') validationFails.add(1);
      else otherErrors.add(1);
    } catch (_) {
      otherErrors.add(1);
    }
  } else if (res.status === 429) {
    rateLimited.add(1);
  } else if (res.status === 422 || res.status === 400) {
    validationFails.add(1);
  } else {
    otherErrors.add(1);
  }
}

export function handleSummary(data) {
  // k6 prints a default summary to stdout — keep it. Also dump JSON for
  // post-run analysis / Grafana ingestion.
  return {
    stdout: textSummary(data),
    'stress-summary.json': JSON.stringify(data, null, 2),
  };
}

// Minimal text summary — k6 ships its own with `--summary-export`, this
// is the trimmed inline version so `handleSummary` stays self-contained.
function textSummary(data) {
  const m = data.metrics;
  const pct = (n) => (n == null ? '—' : Math.round(n));
  const lines = [
    '',
    '== PAM 21G stress summary ==',
    `  iters:        ${pct(m.iterations?.values?.count)}`,
    `  duration:     ${pct(m.iteration_duration?.values?.avg)} ms avg`,
    `  HTTP p50:     ${pct(m.http_req_duration?.values?.['p(50)'])} ms`,
    `  HTTP p95:     ${pct(m.http_req_duration?.values?.['p(95)'])} ms`,
    `  HTTP p99:     ${pct(m.http_req_duration?.values?.['p(99)'])} ms`,
    `  RPS:          ${pct(m.http_reqs?.values?.rate)}`,
    `  accepted:     ${pct(m.pam_accepted?.values?.count)}`,
    `  duplicates:   ${pct(m.pam_duplicates?.values?.count)}`,
    `  rate-limited: ${pct(m.pam_rate_limited?.values?.count)}`,
    `  validation:   ${pct(m.pam_validation_fails?.values?.count)}`,
    `  errors:       ${pct(m.pam_other_errors?.values?.count)}`,
    '',
  ];
  return lines.join('\n');
}
