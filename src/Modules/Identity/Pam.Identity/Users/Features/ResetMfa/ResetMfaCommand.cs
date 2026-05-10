using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Users.Features.ResetMfa;

public sealed record ResetMfaCommand(Guid UserId) : ICommand;
