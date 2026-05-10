namespace Pam.Identity.Contracts.Users.Dtos;

// Public shape returned by /v1/identity/users endpoints. Excludes anything
// the SPA shouldn't see (password hash, security stamp, concurrency stamp,
// access-failed-count internals).
public sealed record BackOfficeUserDto(
    Guid Id,
    string Email,
    Guid? BrandId,
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    bool LockoutEnabled,
    DateTimeOffset? LockoutEnd,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt
);
