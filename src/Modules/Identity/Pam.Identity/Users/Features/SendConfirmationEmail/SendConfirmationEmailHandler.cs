using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Notifications.Contracts.Email;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.SendConfirmationEmail;

// Used in two places:
//   1. POST /v1/identity/users (after creation) — auto-fires so the new
//      user receives their confirmation link immediately.
//   2. POST /v1/identity/users/{id}/send-confirmation-email — admin re-send
//      when the original mail was lost.
//
// Idempotent: re-sending for an already-confirmed user is silently a no-op
// instead of an error. The admin doesn't need to special-case.
public sealed class SendConfirmationEmailHandler(
    UserManager<BackOfficeUser> userManager,
    IEmailSender emailSender,
    IOptions<BackOfficeSpaOptions> spaOptions
) : ICommandHandler<SendConfirmationEmailCommand>
{
    public async Task Handle(
        SendConfirmationEmailCommand command,
        CancellationToken cancellationToken
    )
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            throw new NotFoundException(
                UserErrors.NotFound,
                $"No back-office user found with id '{command.UserId}'."
            );
        }

        if (user.EmailConfirmed || string.IsNullOrEmpty(user.Email))
        {
            return;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var link = QueryHelpers.AddQueryString(
            spaOptions.Value.ConfirmEmailUrl,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["email"] = user.Email,
                ["token"] = token,
            }
        );

        await emailSender.SendAsync(
            new EmailMessage(
                To: user.Email,
                Subject: "Confirm your PAM email",
                TextBody: $"Welcome to PAM.\n\n"
                    + $"Open this link to confirm your email:\n{link}\n\n"
                    + $"If you didn't expect this, ignore this email."
            ),
            cancellationToken
        );
    }
}
