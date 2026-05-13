using FluentAssertions;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Contracts.Vendors;
using Pam.Ingest.Transactions.Events;
using Pam.Ingest.Transactions.Models;
using Xunit;

namespace Pam.Ingest.UnitTests;

// Behavior tests for the VendorTransaction aggregate. The point is the
// invariants — append-only, idempotency-key shape, signed cents — not
// EF mapping (that's integration territory).
public sealed class VendorTransactionTests
{
    [Fact]
    public void Record_creates_received_transaction_and_raises_domain_event()
    {
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        var receivedAt = DateTimeOffset.UtcNow;

        var tx = VendorTransaction.Record(
            id: Guid.CreateVersion7(),
            vendorId: VendorCodes.TwentyOneG,
            vendorReference: "vendor-ref-123",
            brandId: Guid.CreateVersion7(),
            playerId: Guid.CreateVersion7(),
            amountCents: -1500L,
            currency: "USD",
            kind: TransactionKind.Risk,
            occurredAt: occurredAt,
            receivedAt: receivedAt
        );

        tx.Status.Should().Be(TransactionStatus.Received);
        tx.OccurredAt.Should().Be(occurredAt);
        tx.ReceivedAt.Should().Be(receivedAt);
        tx.DomainEvents.Should().ContainSingle(e => e is TransactionIngestedDomainEvent);
    }

    [Fact]
    public void RecordRejected_creates_rejected_transaction_without_event()
    {
        var tx = VendorTransaction.RecordRejected(
            id: Guid.CreateVersion7(),
            vendorId: VendorCodes.TwentyOneG,
            vendorReference: "vendor-ref-456",
            brandId: Guid.CreateVersion7(),
            playerId: Guid.CreateVersion7(),
            amountCents: 2500L,
            currency: "USD",
            kind: TransactionKind.Win,
            occurredAt: DateTimeOffset.UtcNow,
            receivedAt: DateTimeOffset.UtcNow,
            rejectedReason: "unknown_player"
        );

        tx.Status.Should().Be(TransactionStatus.Rejected);
        tx.RejectedReason.Should().Be("unknown_player");
        // Rejected transactions don't raise the integration event — they
        // weren't applied to anything downstream.
        tx.DomainEvents.Should().BeEmpty();
    }
}
