namespace Pam.Players.Infrastructure.Zitadel;

// Singleton holder for values resolved at startup by ZitadelBootstrapService.
// AdminPat is read from disk, Org IDs are looked up by name in ZITADEL, the
// Project is ensured-or-created. ZitadelTokenHandler and BrandRegistry both
// read from this. Mutated only inside ZitadelBootstrapService.StartAsync,
// which runs before the host starts accepting requests.
public sealed class ZitadelRuntimeState
{
    public string AdminPat { get; set; } = "";

    public string ProjectId { get; set; } = "";

    public IDictionary<string, string> BrandOrgIds { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
