namespace Pam.Identity.Authentication.Exceptions;

// Stable error codes surfaced via ProblemDetails.extensions.code. Clients
// program against the codes; messages can be localized or tweaked freely.
internal static class AuthenticationErrors
{
    public const string InvalidCredentials = "identity.login.invalid-credentials";
    public const string LockedOut = "identity.login.locked-out";
    public const string InvalidMfaCode = "identity.login.invalid-mfa-code";
    public const string MfaLockedOut = "identity.login.mfa-locked-out";
    public const string InvalidRecoveryCode = "identity.login.invalid-recovery-code";
    public const string RecoveryCodeLockedOut = "identity.login.recovery-code-locked-out";
}
