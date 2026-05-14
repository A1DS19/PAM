namespace Pam.Shared.Messaging.Outbox;

public sealed class MessagingOutboxOptions
{
    public const string SectionName = "Messaging:Outbox";

    // Cap on rows the BusOutboxDeliveryService pulls per iteration. Bigger
    // batches mean fewer SQL round-trips per published message and higher
    // steady-state drain rate. The trade-off is lock-blast radius:
    // every row in the batch is locked in messaging.outbox_message for the
    // duration of the broker round-trips, so under saturation a larger
    // batch contends harder with the ingest writers. 1000 absorbs a
    // 2-minute burst at ~2× the drain rate cleanly; raise only after
    // observing sustained backlog growth in production.
    public int MessageDeliveryLimit { get; init; } = 1000;

    // Per-batch wall-clock budget for the publish loop. Larger batch needs
    // proportionally more time to round-trip through Rabbit; default is
    // generous enough that even a slow broker doesn't time out mid-batch.
    public TimeSpan MessageDeliveryTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // Idle poll cadence — when there's nothing to deliver. Active sends are
    // woken immediately by BusOutboxNotification, so this only sets the
    // upper bound on post-restart delivery latency for messages left in
    // the outbox by a previous process.
    public TimeSpan QueryDelay { get; init; } = TimeSpan.FromSeconds(60);

    // Window MassTransit considers when checking inbox_state for duplicate
    // message IDs. Wider window catches more retries at the cost of a
    // larger lookup; narrower window risks reprocessing on long retry tails.
    public TimeSpan DuplicateDetectionWindow { get; init; } = TimeSpan.FromMinutes(30);
}
