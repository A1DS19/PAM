using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pam.Operators.Brands.Models;

namespace Pam.Operators.Data.Configurations;

public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Name).IsRequired().HasMaxLength(100);

        builder.Property(b => b.Slug).IsRequired().HasMaxLength(64);
        builder.HasIndex(b => b.Slug).IsUnique();

        builder.Property(b => b.Jurisdiction).IsRequired().HasMaxLength(8);

        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(b => b.CreatedAt).IsRequired();
        builder.Property(b => b.CreatedByType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(b => b.CreatedById).IsRequired().HasMaxLength(128);

        builder.Property(b => b.LastModifiedByType).HasConversion<string>().HasMaxLength(16);
        builder.Property(b => b.LastModifiedById).HasMaxLength(128);

        builder.Ignore(b => b.DomainEvents);
    }
}
