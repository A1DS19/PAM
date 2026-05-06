namespace Pam.Players.Infrastructure.Zitadel;

public sealed class ZitadelOptions
{
    public const string SectionName = "Zitadel";

    public string Authority { get; init; } = default!;

    public string Audience { get; init; } = default!;

    public string ManagementApiBaseUrl { get; init; } = default!;

    public string AdminPat { get; init; } = default!;
}
