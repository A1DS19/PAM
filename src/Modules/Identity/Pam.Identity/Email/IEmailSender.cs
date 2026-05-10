namespace Pam.Identity.Email;

// Minimal email-sending surface used by password-reset + email-confirmation
// flows. Lives inside Pam.Identity because no other module sends mail yet.
// When Pam.Notifications lands this interface gets lifted up to its Contracts
// assembly and the SMTP impl moves with it.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null
);
