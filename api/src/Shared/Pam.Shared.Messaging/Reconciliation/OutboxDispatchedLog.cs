namespace Pam.Shared.Messaging.Reconciliation;

// Append-only record of "this module raised this integration event for this
// business entity." Written by bridge handlers in the same atomic transaction
// as the publish and the business row, so a reconciler can detect orphans
// (business row exists, no matching dispatched-log row) without scanning
// messaging.outbox_message (which is emptied by the delivery service after
// successful publish).
//
// Composite key (module, business_pk, event_type) makes inserts idempotent:
// the reconciler republishes through the same path and the UNIQUE catches
// races between concurrent reconciler runs and natural retries.
public sealed class OutboxDispatchedLog
{
    public required string Module { get; init; }

    public required string BusinessPk { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset DispatchedAt { get; init; }
}
