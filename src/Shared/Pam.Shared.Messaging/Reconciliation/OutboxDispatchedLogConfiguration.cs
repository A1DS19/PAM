using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pam.Shared.Messaging.Reconciliation;

public sealed class OutboxDispatchedLogConfiguration : IEntityTypeConfiguration<OutboxDispatchedLog>
{
    public void Configure(EntityTypeBuilder<OutboxDispatchedLog> builder)
    {
        builder.ToTable("outbox_dispatched_log");

        // PK order matches the reconciler's lookup predicate:
        //   WHERE Module=? AND EventType=? AND BusinessPk IN (...)
        // Putting (Module, EventType) first lets SQL seek to the relevant
        // module+event slice; BusinessPk last serves the IN-list lookup
        // against a contiguous range.
        builder.HasKey(x => new
        {
            x.Module,
            x.EventType,
            x.BusinessPk,
        });

        builder.Property(x => x.Module).HasMaxLength(32);
        builder.Property(x => x.EventType).HasMaxLength(200);
        builder.Property(x => x.BusinessPk).HasMaxLength(64);
        builder.Property(x => x.DispatchedAt);

        // Supports the retention sweep: DELETE TOP (N) WHERE dispatched_at < ?
        // The non-clustered index keeps deletes fast on a multi-million-row
        // table without touching the clustered PK pages.
        builder.HasIndex(x => x.DispatchedAt)
            .HasDatabaseName("IX_outbox_dispatched_log_dispatched_at");
    }
}
