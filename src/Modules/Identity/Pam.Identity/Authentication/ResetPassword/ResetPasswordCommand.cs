using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.ResetPassword;

public sealed record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword
) : ICommand;
