using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Zitadel.Api;
using Zitadel.Management.V1;
using Zitadel.Org.V2;

namespace Pam.Players.Infrastructure.Zitadel;

// Caches gRPC clients from smartive/zitadel-net. Each ManagementService client
// carries its org context as a default header on the underlying HttpClient, so
// we instantiate one per org and memoize. The OrganizationService client has
// no org context (it operates across the instance) and is cached as a single
// instance.
//
// All callers must wait for ZitadelBootstrapService to populate
// state.TokenProvider before using this factory.
public sealed class ZitadelClientFactory(
    IOptions<ZitadelOptions> options,
    ZitadelRuntimeState state
)
{
    private readonly string _endpoint = options.Value.Authority.TrimEnd('/');
    private readonly ConcurrentDictionary<
        string,
        ManagementService.ManagementServiceClient
    > _mgmtByOrg = new(StringComparer.Ordinal);

    private OrganizationService.OrganizationServiceClient? _orgClient;
    private ManagementService.ManagementServiceClient? _mgmtNoOrg;

    public OrganizationService.OrganizationServiceClient OrganizationClient() =>
        _orgClient ??= Clients.OrganizationService(new(_endpoint, RequireTokenProvider()));

    public ManagementService.ManagementServiceClient ManagementClient(string? orgId = null)
    {
        if (string.IsNullOrEmpty(orgId))
        {
            return _mgmtNoOrg ??= Clients.ManagementService(new(_endpoint, RequireTokenProvider()));
        }
        return _mgmtByOrg.GetOrAdd(
            orgId,
            id => Clients.ManagementService(new(_endpoint, RequireTokenProvider()) { Organization = id })
        );
    }

    private ITokenProvider RequireTokenProvider() =>
        state.TokenProvider
        ?? throw new InvalidOperationException(
            "ZITADEL TokenProvider is not initialized — bootstrap has not run."
        );
}
