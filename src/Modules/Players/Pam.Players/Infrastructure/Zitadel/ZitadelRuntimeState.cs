using Zitadel.Api;

namespace Pam.Players.Infrastructure.Zitadel;

// Singleton holder for values resolved at startup by ZitadelBootstrapService.
// TokenProvider is built from a PAT (or, later, JWT-profile service account),
// Org IDs are looked up by name in ZITADEL, the Project is ensured-or-created.
// ZitadelClientFactory and BrandRegistry both read from this. Mutated only
// inside ZitadelBootstrapService.StartAsync, which runs before the host starts
// accepting requests.
public sealed class ZitadelRuntimeState
{
    public ITokenProvider? TokenProvider { get; set; }

    public string ProjectId { get; set; } = "";

    public IDictionary<string, string> BrandOrgIds { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
