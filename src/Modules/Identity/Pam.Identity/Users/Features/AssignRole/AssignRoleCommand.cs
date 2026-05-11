using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.AssignRole;

[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record AssignRoleCommand(Guid UserId, string Role) : ICommand;
