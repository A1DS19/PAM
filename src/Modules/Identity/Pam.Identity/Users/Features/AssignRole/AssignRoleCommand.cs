using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.AssignRole;

public sealed record AssignRoleCommand(Guid UserId, string Role) : ICommand;
