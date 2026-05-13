using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.ResetPassword;

public sealed record ResetPasswordCommand(
    string Email,
    [property: Sensitive] string Token,
    [property: Sensitive] string NewPassword
) : ICommand;
