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

        // nvarchar(max) stores the redacted JSON payload. SQL Server
        // 2016+ supports OPENJSON / JSON_VALUE / JSON_QUERY against
        // arbitrary nvarchar columns, so we keep the same "queryable
        // from sqlcmd / Grafana" property as Postgres jsonb without
        // the type's storage compaction. Storage cost is the
        // worth-it trade for staying within the standard provider.
        builder.Property(x => x.PayloadJson).IsRequired().HasColumnType("nvarchar(max)");

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
