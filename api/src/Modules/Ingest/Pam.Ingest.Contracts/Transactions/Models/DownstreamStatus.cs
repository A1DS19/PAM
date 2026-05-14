namespace Pam.Ingest.Contracts.Transactions.Models;

// Result of the Phase-A intercept forward, when PAM relays a vendor
// callback to GBS and captures the response. Orthogonal to
// TransactionStatus — that one says "what does PAM think happened",
// this one says "what did the upstream say". They CAN disagree (e.g.
// PAM persists a clean Received row even when GBS timed out).
//
// NotApplicable is the right value for vendors where PAM does not
// forward (today: 21G is direct-record only, no upstream forward).
// Once Phase C ships and PAM is wallet-authoritative for a vendor,
// that vendor's rows also stay NotApplicable.
public enum DownstreamStatus
{
    /// PAM did not forward this transaction. Either the vendor does not
    /// use the intercept pattern, or PAM is authoritative (Phase C+).
    NotApplicable,

    /// Forwarded to upstream; a response was received and captured into
    /// downstream_outcome_code / downstream_outcome_message. The response
    /// may itself be a vendor-level rejection — check the code/message.
    Forwarded,

    /// Upstream returned a 5xx. Body may or may not be parseable; what
    /// we could extract is captured, the rest is dropped. Vendor will
    /// typically retry with the same idempotency key.
    UpstreamError,

    /// Upstream did not respond within the per-request budget. We do not
    /// know whether it committed — vendor retries with the same key, and
    /// idempotency at GBS catches the duplicate.
    UpstreamTimeout,

    /// Connection refused, DNS failure, TLS error, etc. The forward
    /// never reached upstream. Safe to retry.
    UpstreamUnreachable,
}
