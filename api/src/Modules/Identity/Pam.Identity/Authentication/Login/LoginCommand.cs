using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Login;

public sealed record LoginCommand(
    string Email,
    [property: Sensitive] string Password,
    bool RememberMe
) : ICommand<LoginResult>;

// Slim result: did the sign-in fully complete, or do we need a second
// factor next? Every other failure mode (bad password, account locked,
// invalid MFA / recovery code) is signalled by the handler throwing —
// CustomExceptionHandler renders those as the standard ProblemDetails
// shape the rest of the API uses.
public sealed record LoginResult(bool Succeeded, bool RequiresTwoFactor);
