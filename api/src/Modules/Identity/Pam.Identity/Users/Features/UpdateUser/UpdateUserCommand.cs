using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.UpdateUser;

// PATCH semantics: each nullable field is "no change when null, set when
// non-null". Clearing BrandId back to null is intentionally not supported
// here — back-office users tied to a Brand stay tied; transfer between
// brands lands as a dedicated endpoint when a customer asks. Status flags
// (EmailConfirmed, TwoFactorEnabled) are mutated by their own endpoints
// (confirm-email, mfa/enroll) because they have side effects.
[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record UpdateUserCommand(
    Guid UserId,
    string? Email,
    Guid? BrandId,
    bool? LockoutEnabled
) : ICommand;
