using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.DeleteUser;

public sealed record DeleteUserCommand(Guid UserId) : ICommand;
