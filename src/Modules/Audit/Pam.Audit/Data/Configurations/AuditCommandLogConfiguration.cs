using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pam.Audit.Models;

namespace Pam.Audit.Data.Configurations;

public sealed class AuditCommandLogConfiguration : IEntityTypeConfiguration<AuditCommandLog>
{
    public void Configure(EntityTypeBuilder<AuditCommandLog> builder)
    {
        builder.ToTable("command_log");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.CorrelationId).HasMaxLength(64);

        builder.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ActorId).IsRequired().HasMaxLength(128);

        builder.Property(x => x.RequestType).IsRequired().HasMaxLength(256);

        // jsonb keeps the payload queryable from psql/Grafana; size is
        // bounded only by Postgres' field cap, and any SELECT can index
        // into specific keys with `payload_json -> 'field'`.
        builder.Property(x => x.PayloadJson).IsRequired().HasColumnType("jsonb");

        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.CompletedAt).IsRequired();
        builder.Property(x => x.DurationMs).IsRequired();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(x => x.ErrorType).HasMaxLength(256);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1024);

        // The canonical operator-activity query: "what did actor X do
        // recently?" — keep it index-served.
        builder.HasIndex(x => new { x.ActorId, x.StartedAt }).IsDescending(false, true);
    }
}
