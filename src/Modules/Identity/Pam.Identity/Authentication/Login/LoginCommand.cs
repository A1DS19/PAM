using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Login;

public sealed record LoginCommand(string Email, string Password, bool RememberMe)
    : ICommand<LoginResult>;

// LoginResult discriminates between failure modes so the endpoint can map to
// the right HTTP status. RequiresTwoFactor will land in PR 2 with the MFA
// challenge endpoints; today the handler treats it as a failure.
public sealed record LoginResult(bool Succeeded, bool IsLockedOut, bool RequiresTwoFactor);
