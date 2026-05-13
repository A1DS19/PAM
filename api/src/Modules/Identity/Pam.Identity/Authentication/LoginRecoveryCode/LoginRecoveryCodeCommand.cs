using Pam.Identity.Authentication.Login;
using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.LoginRecoveryCode;

// Second step of login when the user lost their authenticator. Caller has
// already posted /v1/identity/login and gotten { mfaRequired: true } —
// this consumes a one-time recovery code instead of a TOTP.
public sealed record LoginRecoveryCodeCommand([property: Sensitive] string Code)
    : ICommand<LoginResult>;
