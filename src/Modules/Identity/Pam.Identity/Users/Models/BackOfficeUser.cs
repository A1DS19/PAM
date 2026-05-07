using Microsoft.AspNetCore.Identity;
using Pam.Shared.Contracts.DDD;
using Pam.Shared.Contracts.Identity;

namespace Pam.Identity.Users.Models;

// A back-office (operator) user. Player authentication is deferred to when the
// Players module returns and will likely use a separate scope set against the
// same OpenIddict server (per ADR #16).
//
// IdentityUser<Guid> brings UserName, Email, PasswordHash, LockoutEnd, MFA
// fields, security stamp, etc. We carry BrandId (nullable for platform-admin)
// and the standard PAM audit columns. The audit interceptor stamps the
// columns post-tracker via the IEntity contract — the get-only interface
// signatures are satisfied by the private setters here.
public class BackOfficeUser : IdentityUser<Guid>, IEntity
{
    public Guid? BrandId { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public ActorType CreatedByType { get; private set; }

    public string CreatedById { get; private set; } = default!;

    public DateTimeOffset? LastModifiedAt { get; private set; }

    public ActorType? LastModifiedByType { get; private set; }

    public string? LastModifiedById { get; private set; }
}
