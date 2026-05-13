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
            Status = TransactionStatus.Received,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            RoundId = roundId,
            Description = description,
        };

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
        return new VendorTransaction
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
    }

    // Audit interceptor stamps the audit columns inherited from Entity<TId>
    // (Created* / LastModified*). CreatedBy will be Actor.System most of
    // the time — vendor callbacks are unauthenticated to the PAM JWT
    // layer; the adapter authenticated the vendor itself.
    public static Actor SystemActor => Actor.System;
}
