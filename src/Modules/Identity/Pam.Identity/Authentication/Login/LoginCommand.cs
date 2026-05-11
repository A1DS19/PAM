using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Login;

public sealed record LoginCommand(
    string Email,
    [property: Sensitive] string Password,
    bool RememberMe
) : ICommand<LoginResult>;

// LoginResult discriminates between failure modes so the endpoint can map to
// the right HTTP status. RequiresTwoFactor is a partial success — valid
// credentials, awaiting second factor; the MFA endpoint writes its own
// audit row when the flow completes.
public sealed record LoginResult(bool Succeeded, bool IsLockedOut, bool RequiresTwoFactor)
    : IOperationResult
{
    public string? FailureReason
    {
        get
        {
            if (Succeeded)
            {
                return null;
            }
            if (IsLockedOut)
            {
                return "LockedOut";
            }
            if (RequiresTwoFactor)
            {
                return "RequiresTwoFactor";
            }
            return "InvalidCredentials";
        }
    }
}
