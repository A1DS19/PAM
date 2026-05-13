using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pam.Identity.Users.Models;

namespace Pam.Identity.Data.Configurations;

public sealed class BackOfficeUserConfiguration : IEntityTypeConfiguration<BackOfficeUser>
{
    public void Configure(EntityTypeBuilder<BackOfficeUser> builder)
    {
        // Audit columns. Identity's own columns (UserName, Email,
        // PasswordHash, etc.) are configured by the base IdentityDbContext.
        builder.Property(u => u.CreatedAt).IsRequired();
        builder
            .Property(u => u.CreatedByType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(u => u.CreatedById).IsRequired().HasMaxLength(128);

        builder.Property(u => u.LastModifiedByType).HasConversion<string>().HasMaxLength(16);
        builder.Property(u => u.LastModifiedById).HasMaxLength(128);
    }
}
