namespace Pam.Identity.Email;

// Bound from `Identity:Smtp` in appsettings.{env}.json. Defaults target
// Mailpit at localhost:1025 (no TLS, no auth). Production overrides via
// env vars: Identity__Smtp__Host=… , Identity__Smtp__Port=… , etc.
public sealed class SmtpOptions
{
    public const string SectionName = "Identity:Smtp";

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 1025;

    public bool UseStartTls { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string FromAddress { get; init; } = "no-reply@pam.local";

    public string FromName { get; init; } = "PAM";
}
