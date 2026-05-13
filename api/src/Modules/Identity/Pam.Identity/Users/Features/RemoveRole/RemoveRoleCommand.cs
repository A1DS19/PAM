using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.RemoveRole;

[InvalidateCache("identity:user:*", "identity:users:list:*")]
public sealed record RemoveRoleCommand(Guid UserId, string Role) : ICommand;
