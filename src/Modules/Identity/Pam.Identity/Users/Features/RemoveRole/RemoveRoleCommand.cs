using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.RemoveRole;

public sealed record RemoveRoleCommand(Guid UserId, string Role) : ICommand;
