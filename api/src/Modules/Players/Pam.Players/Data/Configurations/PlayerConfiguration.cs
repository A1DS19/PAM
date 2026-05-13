using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pam.Players.Players.Models;

namespace Pam.Players.Data.Configurations;

public sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("players");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.BrandId).IsRequired();
        builder.HasIndex(p => p.BrandId);

        builder.Property(p => p.Email).IsRequired().HasMaxLength(256);
        // Email uniqueness is per-brand, not global — different brands can
        // legitimately have the same player email (different products).
        builder.HasIndex(p => new { p.BrandId, p.Email }).IsUnique();

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.CreatedByType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.CreatedById).IsRequired().HasMaxLength(128);

        builder.Property(p => p.LastModifiedByType).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.LastModifiedById).HasMaxLength(128);

        builder.Ignore(p => p.DomainEvents);
    }
}
