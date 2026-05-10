using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
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
public sealed class ForgotPasswordHandler(
    UserManager<BackOfficeUser> userManager,
    IEmailSender emailSender,
    IOptions<BackOfficeSpaOptions> spaOptions
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

        await emailSender.SendAsync(
            new EmailMessage(
                To: command.Email,
                Subject: "Reset your PAM password",
                TextBody:
                    $"You requested a password reset.\n\n" +
                    $"Open this link to choose a new password:\n{link}\n\n" +
                    $"If you didn't request this, ignore this email."
            ),
            cancellationToken
        );
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
