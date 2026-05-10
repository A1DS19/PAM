using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Pam.Identity.Email;

// MailKit-over-SMTP sender. Defaults wired for the Mailpit dev container
// (plain TCP, no TLS, no auth). Production sets Identity:Smtp:Host +
// :UseStartTls=true + credentials via env vars; the same impl carries over.
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger
) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var opts = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(opts.FromName, opts.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { TextBody = message.TextBody };
        if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            body.HtmlBody = message.HtmlBody;
        }
        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        var secureSocketOptions = opts.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(opts.Host, opts.Port, secureSocketOptions, cancellationToken);
        if (!string.IsNullOrEmpty(opts.Username))
        {
            await client.AuthenticateAsync(opts.Username, opts.Password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation(
            "Sent email to {To} via {Host}:{Port}",
            message.To,
            opts.Host,
            opts.Port
        );
    }
}
