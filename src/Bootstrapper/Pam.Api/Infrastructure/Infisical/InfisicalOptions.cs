namespace Pam.Api.Infrastructure.Infisical;

public sealed class InfisicalOptions
{
    public const string SectionName = "Infisical";

    public string? Host { get; set; }

    public string? ProjectId { get; set; }

    public string? Environment { get; set; } = "dev";

    public string? SecretPath { get; set; } = "/";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool Optional { get; set; } = true;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal bool IsConfigured =>
        !string.IsNullOrEmpty(Host)
        && !string.IsNullOrEmpty(ProjectId)
        && !string.IsNullOrEmpty(ClientId)
        && !string.IsNullOrEmpty(ClientSecret);
}
