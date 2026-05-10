using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.UnlockUser;

public sealed record UnlockUserCommand(Guid UserId) : ICommand;
