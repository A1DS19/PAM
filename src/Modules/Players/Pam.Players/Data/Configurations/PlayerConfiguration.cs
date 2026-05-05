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
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PlayerId(value))
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(p => p.IdentityProviderId)
            .HasColumnName("identity_provider_id")
            .HasMaxLength(64)
            .IsRequired();
        builder.HasIndex(p => p.IdentityProviderId).IsUnique();

        builder.OwnsOne(
            p => p.Email,
            email =>
            {
                email.Property(e => e.Value).HasColumnName("email").HasMaxLength(254).IsRequired();
                email.HasIndex(e => e.Value).IsUnique();
            }
        );

        builder.OwnsOne(
            p => p.Name,
            name =>
            {
                name.Property(n => n.First)
                    .HasColumnName("first_name")
                    .HasMaxLength(80)
                    .IsRequired();
                name.Property(n => n.Last).HasColumnName("last_name").HasMaxLength(80).IsRequired();
                name.Property(n => n.Middle).HasColumnName("middle_name").HasMaxLength(80);
            }
        );

        builder.OwnsOne(
            p => p.DateOfBirth,
            dob =>
            {
                dob.Property(d => d.Value).HasColumnName("date_of_birth").IsRequired();
            }
        );

        builder.OwnsOne(
            p => p.Jurisdiction,
            j =>
            {
                j.Property(x => x.CountryCode)
                    .HasColumnName("country_code")
                    .HasMaxLength(2)
                    .IsRequired();
                j.Property(x => x.Region).HasColumnName("region").HasMaxLength(8);
            }
        );

        builder.Property(p => p.Status).HasColumnName("status").HasConversion<int>().IsRequired();

        builder.Property(p => p.EmailVerified).HasColumnName("email_verified").IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedBy).HasColumnName("created_by").HasMaxLength(64);
        builder.Property(p => p.LastModifiedAt).HasColumnName("last_modified_at");
        builder.Property(p => p.LastModifiedBy).HasColumnName("last_modified_by").HasMaxLength(64);

        builder.Ignore(p => p.DomainEvents);
    }
}
