using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pam.Identity.Permissions.Models;
using Pam.Identity.Users.Models;

namespace Pam.Identity.Data;

// All Pam.Identity tables live in schema `identity`:
//   ASP.NET Core Identity:  AspNetUsers, AspNetRoles, AspNetUserRoles,
//                           AspNetUserClaims, AspNetRoleClaims,
//                           AspNetUserLogins, AspNetUserTokens
//   OpenIddict (via UseOpenIddict on the options):
//                           OpenIddictApplications, OpenIddictAuthorizations,
//                           OpenIddictScopes, OpenIddictTokens
//   Custom (this module):   permissions, role_permissions
//   Data Protection:        data_protection_keys (master keyring; shared
//                           across replicas so cookies + OpenIddict-issued
//                           opaque strings round-trip across instances)
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<BackOfficeUser, IdentityRole<Guid>, Guid>(options),
        IDataProtectionKeyContext
{
    public const string Schema = "identity";

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ASP.NET Core Identity sets explicit table names ("AspNetUsers" etc.)
        // via fluent API, which the snake_case naming convention doesn't
        // override. Rename to match the rest of the schema. OpenIddict's
        // PascalCase tables (OpenIddictApplications, …) stay as-is — they're
        // framework-managed and rarely touched from psql.
        builder.Entity<BackOfficeUser>().ToTable("users");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        builder.HasDefaultSchema(Schema);
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
