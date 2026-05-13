using System.Diagnostics.CodeAnalysis;

namespace Pam.Identity;

// Configures the back-office SPA's OpenIddict application descriptor + the
// cookie-challenge redirect when an unauthenticated browser hits the OIDC
// authorize endpoint. Bound from `Identity:BackOfficeSpa` in appsettings.
public sealed class BackOfficeSpaOptions
{
    public const string SectionName = "Identity:BackOfficeSpa";

    public string ClientId { get; init; } = "pam-bo";

    public string DisplayName { get; init; } = "PAM Back-Office SPA";

    // Where the cookie middleware redirects the browser when the user hits
    // /connect/authorize without a session. Carries `?returnUrl=` so the SPA
    // can navigate back into the OIDC flow after login.
    public string LoginUrl { get; init; } = "http://localhost:3000/login";

    // Where the password-reset email link lands. The SPA reads `?email=` +
    // `?token=` from the query, lets the user pick a new password, then
    // posts to POST /v1/identity/reset-password.
    public string ResetPasswordUrl { get; init; } = "http://localhost:3000/reset-password";

    // Where the email-confirmation link lands. The SPA reads `?email=` +
    // `?token=` and posts to POST /v1/identity/me/confirm-email.
    public string ConfirmEmailUrl { get; init; } = "http://localhost:3000/confirm-email";

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Required for correct IConfiguration binding semantics."
    )]
    public string[] RedirectUris { get; init; } = ["http://localhost:3000/auth/callback"];

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Required for correct IConfiguration binding semantics."
    )]
    public string[] PostLogoutRedirectUris { get; init; } = ["http://localhost:3000/"];
}
