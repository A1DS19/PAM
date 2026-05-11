using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pam.Wallet.Accounts.Models;

namespace Pam.Wallet.Data.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.BrandId).IsRequired();
        builder.HasIndex(a => a.BrandId);

        builder.Property(a => a.PlayerId).IsRequired();
        // A player has at most one account per currency per brand. The
        // real wallet model will introduce account *types* (cash, bonus,
        // …) — re-derive this index when those land.
        builder.HasIndex(a => new { a.BrandId, a.PlayerId, a.Currency }).IsUnique();

        // ISO 4217 — three letters. Stored fixed-width so a future check
        // constraint can pin the format.
        builder.Property(a => a.Currency).IsRequired().HasMaxLength(3).IsFixedLength();

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.CreatedByType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.CreatedById).IsRequired().HasMaxLength(128);

        builder.Property(a => a.LastModifiedByType).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.LastModifiedById).HasMaxLength(128);

        builder.Ignore(a => a.DomainEvents);
    }
}
