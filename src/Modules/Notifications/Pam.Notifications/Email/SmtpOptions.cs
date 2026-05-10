namespace Pam.Notifications.Email;

// Bound from `Notifications:Smtp` in appsettings.{env}.json. Defaults
// target Mailpit at localhost:1025 (no TLS, no auth). Production
// overrides via env vars: Notifications__Smtp__Host=… , etc.
internal sealed class SmtpOptions
{
    public const string SectionName = "Notifications:Smtp";

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 1025;

    public bool UseStartTls { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string FromAddress { get; init; } = "no-reply@pam.local";

    public string FromName { get; init; } = "PAM";
}
