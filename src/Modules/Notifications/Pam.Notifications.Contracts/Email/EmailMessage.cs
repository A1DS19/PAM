namespace Pam.Notifications.Contracts.Email;

public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null
);
