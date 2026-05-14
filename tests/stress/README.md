# Stress

Stress / load scripts for hot paths in PAM. Not part of the .NET solution.
Run against a fully wired `docker compose up` stack (mssql + rabbitmq +
redis + otel-lgtm) — Testcontainers-style isolation is intentionally not
used so the reconciler, outbox delivery service, and broker all
participate in the load.

## Prerequisites

- [k6](https://k6.io/) on the host (`brew install k6`)
- `docker compose up -d` running (mssql, rabbitmq, redis, otel-lgtm)
- API running in `Stress` environment (see below)

## What "Stress" environment changes

`appsettings.Stress.json` flips three things:

| Setting | Why |
|---|---|
| `RateLimiting:Enabled=false` | The `api-default` 100 req / 30s limiter would dominate the numbers. Policies become no-op partitions; endpoint chains stay valid. |
| `Stress:FastSpinUpstreamStub:Enabled=true` | Swaps the GBS-forwarding `FastSpinUpstream` for `StubFastSpinUpstream`, which returns a canned 200 with no outbound HTTP. Without it, `fastspin.js` would generate load on the real dev GBS and the numbers would be dominated by their network. |
| `Messaging:Reconciliation:Interval=00:01:00` | Drops the 5-min default so reconciler behaviour is observable during a 2-3 minute run. `MinAge` drops to 30s for the same reason. |

The integration-event discard subscribers in
`Pam.Notifications.Subscribers` are registered in **every** environment
— they bind a queue to the exchange so events are observable / replayable
even before a real consumer ships. No flag required.

## Running

Open three terminals.

```bash
# 1. infra
make stress-up

# 2. API in Stress mode
make stress-api

# 3. (after a clean run is wanted) reset tables, then run
make stress-reset
make stress-21g                       # default: VUS=50 DURATION=90s
make stress-21g VUS=100 DURATION=3m   # turn it up
make stress-fastspin                  # same shape, intercept path
```

The k6 summary lands at `tests/stress/stress-summary.json` and prints a
short text summary to stdout. Grafana at `http://localhost:3001` shows
the API's OTel metrics in real time.

## What to watch

| Signal | Where | Healthy |
|---|---|---|
| HTTP p95 latency | k6 stdout, Grafana `http.server.request.duration` | < 200 ms on local dev hw, < 50 ms on CI Linux |
| Throughput | k6 `http_reqs.rate` | matches VU * (1 / p50) approximately |
| Outbox backlog | `SELECT COUNT(*) FROM messaging.outbox_message` (during run) | near 0; trending up means delivery is slower than ingest |
| Dispatched-log growth | `SELECT COUNT(*) FROM messaging.outbox_dispatched_log` | grows 1:1 with `pam_accepted` |
| Queue depth | RabbitMQ management UI :15672 | bound queues for discard consumers should drain |
| Reconciler republishes | API logs ("Reconciler … republished") | 0 under healthy load — any non-zero means COMMIT #1 happened without COMMIT #2 |
| `pam_accepted` counter | k6 stdout | matches VU output |
| `pam_duplicates` counter | k6 stdout | 0 — if non-zero, `randomString(6)` collided (raise it or include `__VU-__ITER`) |
| `pam_rate_limited` counter | k6 stdout | 0 — non-zero means you forgot `ASPNETCORE_ENVIRONMENT=Stress` |

## Scenarios

`21g.js` runs the fresh-insert path against the 21G REST adapter.
`fastspin.js` runs the same fresh-insert shape against the FastSpin
Phase-A intercept endpoint (`/v1/ingest/vendors/fastspin/main`). The
intercept normally forwards every request to GBS; in Stress mode the
upstream is replaced with `StubFastSpinUpstream` (a canned 200), so
the measurement stays on PAM's hot path: HTTP → adapter → handler →
COMMIT #1 → COMMIT #2 → outbox delivery.

To exercise other shapes, clone the file and change the payload generator:

- **Idempotent retries:** reuse a fixed pool of references. Expect
  ~1× `pam_accepted` followed by sustained `pam_duplicates`.
- **Validation rejects:** send `currency: "XYZ"` or
  `occurredAt: <100 years from now>`. Expect 422s, exercises
  `ValidationBehavior` + `ProblemDetails`.
- **Burst:** stages `0 → 10×VUS for 30s → VUS for 60s`. Tests outbox
  flush under spike. Run with `RateLimiting:Enabled=true` to also see
  the limiter in action.

Keep scenarios in separate files. Mixing them obscures which path each
metric belongs to.
