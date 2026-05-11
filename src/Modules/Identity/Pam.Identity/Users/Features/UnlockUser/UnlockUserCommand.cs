using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.UnlockUser;

[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record UnlockUserCommand(Guid UserId) : ICommand;
