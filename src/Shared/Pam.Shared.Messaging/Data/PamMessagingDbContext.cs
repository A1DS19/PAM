using MassTransit;
using Microsoft.EntityFrameworkCore;
using Pam.Shared.Messaging.Reconciliation;

namespace Pam.Shared.Messaging.Data;

// Single home for MassTransit's transactional-outbox tables (inbox_state,
// outbox_state, outbox_message) across the entire monolith. The single
// .UseBusOutbox() registration in AddPamMassTransit binds this context as
// the bus-wide outbox target — every IPublishEndpoint.Publish call from any
// module's bridge handler routes its OutboxMessage row into this DbContext's
// change tracker, regardless of which module's business save is currently
// running.
//
// Why a shared context, not per-module: MassTransit 8.5.x can only attach
// one DbContext to a given bus via UseBusOutbox (the v9.1+ multi-DbContext
// overload is off-limits while we hold the Apache-2.0 line). The MT author
// endorses a single dedicated messaging DbContext as the canonical fix for
// multi-module monoliths — see DECISIONS.md ADR on the outbox topology and
// https://github.com/MassTransit/MassTransit/discussions/5480.
//
// Schema "messaging" is namespaced separately from any module schema. The
// migration history table lives at messaging.__EFMigrationsHistory.
public sealed class PamMessagingDbContext(DbContextOptions<PamMessagingDbContext> options)
    : DbContext(options)
{
    public const string Schema = "messaging";

    // Reconciliation log written by bridge handlers in the same atomic
    // transaction as the publish; queried by OutboxReconciliationService to
    // detect business rows whose integration event never landed.
    public DbSet<OutboxDispatchedLog> DispatchedLog => Set<OutboxDispatchedLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.ApplyConfiguration(new OutboxDispatchedLogConfiguration());
    }
}
