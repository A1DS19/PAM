using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Pam.Shared.Messaging.Reconciliation;

public sealed class OutboxDispatchedLogConfiguration : IEntityTypeConfiguration<OutboxDispatchedLog>
{
    public void Configure(EntityTypeBuilder<OutboxDispatchedLog> builder)
    {
        builder.ToTable("outbox_dispatched_log");

        builder.HasKey(x => new
        {
            x.Module,
            x.BusinessPk,
            x.EventType,
        });

        builder.Property(x => x.Module).HasMaxLength(32);
        builder.Property(x => x.BusinessPk).HasMaxLength(64);
        builder.Property(x => x.EventType).HasMaxLength(200);
        builder.Property(x => x.DispatchedAt);
    }
}
