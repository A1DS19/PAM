namespace Pam.Players.Infrastructure.Zitadel;

public sealed class ZitadelOptions
{
    public const string SectionName = "Zitadel";

    public string Authority { get; init; } = default!;

    public string Audience { get; init; } = default!;

    public string ManagementApiBaseUrl { get; init; } = default!;

    // Path to the PAT file ZITADEL writes on first init via
    // ZITADEL_FIRSTINSTANCE_PATPATH. Resolved relative to the working
    // directory or — if not found — by walking upward to find the repo
    // root (pam.slnx or .git marker).
    public string AdminPatFile { get; init; } = default!;

    public string ProjectName { get; init; } = "pam-player-api";
}
