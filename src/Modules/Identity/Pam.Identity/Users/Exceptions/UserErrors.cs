namespace Pam.Identity.Users.Exceptions;

// Stable error codes surfaced via ProblemDetails.extensions.code. Clients
// program against the codes; messages can be localized and tweaked freely.
internal static class UserErrors
{
    public const string EmailTaken = "identity.user.email-taken";
    public const string NotFound = "identity.user.not-found";
    public const string RoleNotFound = "identity.user.role-not-found";
    public const string CreateFailed = "identity.user.create-failed";
    public const string UpdateFailed = "identity.user.update-failed";
    public const string DeleteFailed = "identity.user.delete-failed";
    public const string PasswordChangeFailed = "identity.user.password-change-failed";
    public const string MfaEnrollFailed = "identity.user.mfa-enroll-failed";
    public const string MfaVerifyFailed = "identity.user.mfa-verify-failed";
}
