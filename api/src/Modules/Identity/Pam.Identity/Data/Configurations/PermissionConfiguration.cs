using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pam.Identity.Permissions.Models;

namespace Pam.Identity.Data.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Code).IsRequired().HasMaxLength(128);
        builder.HasIndex(p => p.Code).IsUnique();

        builder.Property(p => p.Description).HasMaxLength(256);
    }
}
