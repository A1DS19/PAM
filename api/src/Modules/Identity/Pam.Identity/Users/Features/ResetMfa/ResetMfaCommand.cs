using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.ResetMfa;

[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record ResetMfaCommand(Guid UserId) : ICommand;
