using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Transactions.Events;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.DDD;

namespace Pam.Ingest.Transactions.Models;

// Append-only fact. Every successful vendor callback produces exactly one
// row in ingest.vendor_transactions; the row's content never changes after
// it's written. The (VendorId, VendorReference) UNIQUE constraint is the
// idempotency guarantee — re-POSTs from a retrying vendor hit the unique
// index and we surface that as TransactionStatus.Duplicate without
// persisting a second row.
//
// Amount semantics: signed cents in a bigint column. Risk (debit) is
// stored negative; Win (credit) positive. Never floating-point — float
// money is one of the known GBS bugs we're explicitly fixing in PAM.
public sealed class VendorTransaction : Aggregate<Guid>
{
    public string VendorId { get; private set; } = default!;

    public string VendorReference { get; private set; } = default!;

    public Guid BrandId { get; private set; }

    public Guid PlayerId { get; private set; }

    public long AmountCents { get; private set; }

    public string Currency { get; private set; } = default!;

    public TransactionKind Kind { get; private set; }

    public TransactionStatus Status { get; private set; }

    public string? RoundId { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public string? RejectedReason { get; private set; }

    // ---- Phase-A intercept capture (filled by FastSpinAdapter when PAM
    // forwards to GBS; null for vendors that don't use the intercept
    // pattern). See DownstreamStatus for the semantics. ----

    // Player's balance after the transaction, as reported by upstream.
    // Signed cents in a bigint column — same convention as AmountCents.
    // Null when no response was captured (UpstreamTimeout/Unreachable).
    public long? VendorBalanceAfterCents { get; private set; }

    // GBS DocumentNumber / merchantTxId — the upstream's own primary
    // key for this transaction. Lets reports cross-reference back into
    // tbcasinoplaytoday during the strangler-fig window.
    public string? DownstreamReference { get; private set; }

    // Upstream's `code` field (0 = success, anything else = vendor-level
    // rejection at GBS — bad currency, insufficient balance, etc.).
    public int? DownstreamOutcomeCode { get; private set; }

    public string? DownstreamOutcomeMessage { get; private set; }

    public DownstreamStatus DownstreamStatus { get; private set; }

    // Wall-clock time of the forward in milliseconds — feeds SLA
    // dashboards ("p95 of GBS latency through PAM"). Null when no
    // forward was attempted (NotApplicable) or wasn't measurable.
    public int? DownstreamLatencyMs { get; private set; }

    private VendorTransaction() { }

    public static VendorTransaction Record(
        Guid id,
        string vendorId,
        string vendorReference,
        Guid brandId,
        Guid playerId,
        long amountCents,
        string currency,
        TransactionKind kind,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        string? roundId = null,
        string? description = null,
        // Phase-A intercept fields. Defaults preserve existing
        // non-intercept call sites (21G, tests, future authoritative
        // vendors) — they create rows with DownstreamStatus.NotApplicable
        // and all downstream_* columns null. Intercept callers
        // (FastSpinAdapter) pass real values.
        long? vendorBalanceAfterCents = null,
        string? downstreamReference = null,
        int? downstreamOutcomeCode = null,
        string? downstreamOutcomeMessage = null,
        DownstreamStatus downstreamStatus = DownstreamStatus.NotApplicable,
        int? downstreamLatencyMs = null
    )
    {
        var tx = new VendorTransaction
        {
            Id = id,
            VendorId = vendorId,
            VendorReference = vendorReference,
            BrandId = brandId,
            PlayerId = playerId,
            AmountCents = amountCents,
            Currency = currency,
            Kind = kind,
            Status = TransactionStatus.Received,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            RoundId = roundId,
            Description = description,
            VendorBalanceAfterCents = vendorBalanceAfterCents,
            DownstreamReference = downstreamReference,
            DownstreamOutcomeCode = downstreamOutcomeCode,
            DownstreamOutcomeMessage = downstreamOutcomeMessage,
            DownstreamStatus = downstreamStatus,
            DownstreamLatencyMs = downstreamLatencyMs,
        };

        // Inline audit stamp — IngestDbContext does NOT register
        // AuditableSaveChangesInterceptor (see IngestModule), so we set
        // the columns ourselves. Actor is (Service, vendorId) which is
        // both faster (no IUserContext lookup per save) and more
        // accurate than the interceptor's Actor.Anonymous default —
        // now you can audit-trail back to which vendor wrote the row.
        tx.Stamp(receivedAt, new Actor(ActorType.Service, vendorId));

        tx.RaiseDomainEvent(
            new TransactionIngestedDomainEvent(
                tx.Id,
                tx.VendorId,
                tx.VendorReference,
                tx.BrandId,
                tx.PlayerId,
                tx.AmountCents,
                tx.Currency,
                tx.Kind,
                tx.Status,
                tx.RoundId,
                tx.OccurredAt
            )
        );

        return tx;
    }

    // Reject is a state-at-creation, not a transition — a transaction can
    // be born Rejected (validation failed) but never moves into Rejected
    // from a Received state.
    public static VendorTransaction RecordRejected(
        Guid id,
        string vendorId,
        string vendorReference,
        Guid brandId,
        Guid playerId,
        long amountCents,
        string currency,
        TransactionKind kind,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        string rejectedReason,
        string? roundId = null,
        string? description = null
    )
    {
        var tx = new VendorTransaction
        {
            Id = id,
            VendorId = vendorId,
            VendorReference = vendorReference,
            BrandId = brandId,
            PlayerId = playerId,
            AmountCents = amountCents,
            Currency = currency,
            Kind = kind,
            Status = TransactionStatus.Rejected,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            RoundId = roundId,
            Description = description,
            RejectedReason = rejectedReason,
        };
        tx.Stamp(receivedAt, new Actor(ActorType.Service, vendorId));
        return tx;
    }
}
