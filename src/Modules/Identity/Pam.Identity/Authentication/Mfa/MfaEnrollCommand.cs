using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Mfa;

public sealed record MfaEnrollCommand : ICommand<MfaEnrollResult>;

// SharedKey is the base32-encoded TOTP secret the user types into an
// authenticator that can't scan QR codes. AuthenticatorUri is the
// otpauth:// URI for QR rendering by the SPA; both encode the same secret.
public sealed record MfaEnrollResult(string SharedKey, Uri AuthenticatorUri);
