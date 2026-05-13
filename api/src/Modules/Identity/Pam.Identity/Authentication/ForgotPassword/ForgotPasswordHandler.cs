using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pam.Identity.Users.Models;
using Pam.Notifications.Contracts.Email;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.ForgotPassword;

// Email-enumeration-safe: the endpoint ALWAYS returns 204, whether or not
// the email matches a real user. The handler does the lookup and only
// sends the email when the user exists + has a confirmed email. Anyone
// timing the response can still infer existence; we accept that — adding
// a fixed delay to mask timing is more theatre than security.
//
// The SMTP send is wrapped in try/catch so an SMTP outage doesn't break
// the anti-enumeration shape: without the catch, unknown emails 204 while
// known emails 500 once SMTP is down, which is exactly the signal we're
// trying to deny. Identity's emails stay synchronous (the reset token is
// a credential — see ARCHITECTURE.md); the trade-off is that on transient
// SMTP failures the user sees no error and has to try again later.
public sealed class ForgotPasswordHandler(
    UserManager<BackOfficeUser> userManager,
    IEmailSender emailSender,
    IOptions<BackOfficeSpaOptions> spaOptions,
    ILogger<ForgotPasswordHandler> logger
) : ICommandHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(command.Email);
        if (user is null || !await userManager.IsEmailConfirmedAsync(user))
        {
            // Silent no-op — don't tell the caller whether the address exists.
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = BuildLink(spaOptions.Value.ResetPasswordUrl, command.Email, token);

        try
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    To: command.Email,
                    Subject: "Reset your PAM password",
                    TextBody: $"You requested a password reset.\n\n"
                        + $"Open this link to choose a new password:\n{link}\n\n"
                        + $"If you didn't request this, ignore this email."
                ),
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            // Swallow so the response stays 204 regardless of SMTP state.
            // Email address NOT logged — that's the very PII this endpoint
            // is designed to protect from probing.
            logger.LogError(ex, "Failed to send password-reset email; caller still receives 204");
        }
    }

    private static string BuildLink(string baseUrl, string email, string token)
    {
        // QueryHelpers.AddQueryString handles per-value escaping. The token
        // is base64ish with `+`, `/`, `=` — these get percent-encoded so the
        // SPA can read them back via standard URL parsing.
        return QueryHelpers.AddQueryString(
            baseUrl,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["email"] = email,
                ["token"] = token,
            }
        );
    }
}
