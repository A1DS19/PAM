using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Transactions.Models;

namespace Pam.Ingest.Data.Configurations;

public sealed class VendorTransactionConfiguration : IEntityTypeConfiguration<VendorTransaction>
{
    public void Configure(EntityTypeBuilder<VendorTransaction> builder)
    {
        builder.ToTable("vendor_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.VendorId).IsRequired().HasMaxLength(32);

        // Vendor's transaction id — the idempotency key. GBS allows 400
        // chars in tbCasinoPlayToday.Reference; mirror that ceiling so we
        // never lose a vendor's id during migration.
        builder.Property(t => t.VendorReference).IsRequired().HasMaxLength(400);

        builder.Property(t => t.BrandId).IsRequired();
        builder.Property(t => t.PlayerId).IsRequired();

        // bigint signed cents. NEVER use double/decimal here — float
        // money is one of the known GBS bugs we're explicitly fixing.
        builder.Property(t => t.AmountCents).IsRequired();

        // ISO 4217.
        builder.Property(t => t.Currency).IsRequired().HasMaxLength(3).IsFixedLength();

        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(t => t.RoundId).HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(250);

        builder.Property(t => t.OccurredAt).IsRequired();
        builder.Property(t => t.ReceivedAt).IsRequired();

        builder.Property(t => t.RejectedReason).HasMaxLength(64);

        // Phase-A intercept capture columns. All nullable except
        // downstream_status, which defaults to NotApplicable so legacy
        // rows + non-intercept vendors land cleanly without backfill.
        builder.Property(t => t.VendorBalanceAfterCents);
        builder.Property(t => t.DownstreamReference).HasMaxLength(64);
        builder.Property(t => t.DownstreamOutcomeCode);
        builder.Property(t => t.DownstreamOutcomeMessage).HasMaxLength(256);
        builder
            .Property(t => t.DownstreamStatus)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired()
            .HasDefaultValue(DownstreamStatus.NotApplicable);
        builder.Property(t => t.DownstreamLatencyMs);

        // The idempotency guarantee. A vendor retrying with the same
        // (vendor_id, vendor_reference) trips this unique index; the
        // handler catches SQL Server error 2627/2601 (unique-key violation) and surfaces
        // TransactionStatus.Duplicate.
        builder
            .HasIndex(t => new { t.VendorId, t.VendorReference })
            .IsUnique()
            .HasDatabaseName("ix_vendor_transactions_idempotency");

        // The canonical "unified transaction view" query — show me this
        // player's transactions across all vendors, newest first.
        builder
            .HasIndex(t => new
            {
                t.BrandId,
                t.PlayerId,
                t.OccurredAt,
            })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_vendor_transactions_player_timeline");

        // Vendor-scoped queries (reconciliation reports per vendor).
        builder.HasIndex(t => new { t.VendorId, t.OccurredAt }).IsDescending(false, true);

        // Reconciler scan window: `WHERE received_at BETWEEN ? AND ?
        // AND status != 'Rejected' ORDER BY received_at LIMIT 200`. At
        // millions of rows/day this index keeps each pass O(window-size)
        // instead of O(table-size). status is the second key column so
        // the (received_at, status) seek skips Rejected rows without a
        // key-lookup. Future scale work: time-based partitioning on
        // received_at lets old partitions age out to slower storage
        // without touching this index. See ARCHITECTURE.md.
        builder
            .HasIndex(t => new { t.ReceivedAt, t.Status })
            .HasDatabaseName("ix_vendor_transactions_received_at_status");

        // Audit columns inherited from Entity<TId>.
        builder.Property(t => t.CreatedAt).IsRequired();
        builder
            .Property(t => t.CreatedByType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(t => t.CreatedById).IsRequired().HasMaxLength(128);

        builder.Property(t => t.LastModifiedByType).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.LastModifiedById).HasMaxLength(128);

        builder.Ignore(t => t.DomainEvents);
    }
}
