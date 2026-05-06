using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pam.Players.Players.Brands;
using Zitadel.Api;
using Zitadel.Management.V1;
using Zitadel.Org.V2;
// V1 and V2 each ship a TextQueryMethod enum; alias to keep call sites
// unambiguous. Org V2 search uses Object.V2; Project V1 search uses V1.
using OrgTextQueryMethod = Zitadel.Object.V2.TextQueryMethod;
using ProjectTextQueryMethod = Zitadel.V1.TextQueryMethod;

namespace Pam.Players.Infrastructure.Zitadel;

public sealed class ZitadelBootstrapService(
    IHttpClientFactory httpFactory,
    ZitadelClientFactory clientFactory,
    ZitadelRuntimeState state,
    IOptions<ZitadelOptions> zitadelOpts,
    IOptions<BrandRegistryOptions> brandOpts,
    ILogger<ZitadelBootstrapService> logger
) : IHostedService
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(2);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var z = zitadelOpts.Value;
        var b = brandOpts.Value;

        await WaitForZitadelReadyAsync(z.Authority, cancellationToken);

        var patPath = ResolvePatFile(z.AdminPatFile);
        var pat = (await File.ReadAllTextAsync(patPath, cancellationToken)).Trim();
        if (string.IsNullOrEmpty(pat))
        {
            throw new InvalidOperationException($"ZITADEL admin PAT file at '{patPath}' is empty.");
        }

        // The lib's StaticTokenProvider attaches the PAT as a Bearer header on
        // every gRPC call via its internal DelegatingHandler. JWT-profile
        // (ITokenProvider.ServiceAccount) is the prod path — swap by setting
        // ZitadelOptions and constructing a different provider here.
        state.TokenProvider = ITokenProvider.Static(pat);

        var orgClient = clientFactory.OrganizationClient();
        foreach (var brandId in b.BrandIds)
        {
            var orgId = await EnsureOrgAsync(orgClient, brandId, cancellationToken);
            state.BrandOrgIds[brandId] = orgId;
            logger.LogInformation("Brand {BrandId} → ZITADEL Org {OrgId}", brandId, orgId);
        }

        if (!state.BrandOrgIds.TryGetValue(b.Default, out var defaultOrg))
        {
            throw new InvalidOperationException(
                $"Default brand '{b.Default}' is not in Brands:BrandIds."
            );
        }
        var mgmt = clientFactory.ManagementClient(defaultOrg);
        state.ProjectId = await EnsureProjectAsync(mgmt, z.ProjectName, cancellationToken);
        logger.LogInformation(
            "ZITADEL Project '{Name}' → {ProjectId}",
            z.ProjectName,
            state.ProjectId
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task WaitForZitadelReadyAsync(string authority, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient("zitadel-readiness");
        var url = new Uri(authority.TrimEnd('/') + "/.well-known/openid-configuration");

        var deadline = DateTimeOffset.UtcNow + ReadinessTimeout;
        var lastError = "";
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await http.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode)
                {
                    return;
                }
                lastError = $"HTTP {(int)resp.StatusCode}";
            }
            catch (HttpRequestException ex)
            {
                lastError = ex.Message;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        throw new InvalidOperationException(
            $"ZITADEL did not become ready at {url} within {ReadinessTimeout.TotalSeconds:0}s. Last error: {lastError}"
        );
    }

    private static async Task<string> EnsureOrgAsync(
        OrganizationService.OrganizationServiceClient client,
        string name,
        CancellationToken ct
    )
    {
        try
        {
            var resp = await client.AddOrganizationAsync(
                new AddOrganizationRequest { Name = name },
                cancellationToken: ct
            );
            return resp.OrganizationId;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            // Fall through to search by name.
        }

        var search = new ListOrganizationsRequest();
        search.Queries.Add(
            new SearchQuery
            {
                NameQuery = new OrganizationNameQuery
                {
                    Name = name,
                    Method = OrgTextQueryMethod.Equals,
                },
            }
        );
        var found = await client.ListOrganizationsAsync(search, cancellationToken: ct);
        if (found.Result.Count == 0)
        {
            throw new InvalidOperationException(
                $"ZITADEL search for org '{name}' returned no results, but create reported a conflict."
            );
        }
        return found.Result[0].Id;
    }

    private static async Task<string> EnsureProjectAsync(
        ManagementService.ManagementServiceClient client,
        string name,
        CancellationToken ct
    )
    {
        try
        {
            var resp = await client.AddProjectAsync(
                new AddProjectRequest { Name = name },
                cancellationToken: ct
            );
            return resp.Id;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            // Fall through to search by name.
        }

        var search = new ListProjectsRequest();
        search.Queries.Add(
            new global::Zitadel.Project.V1.ProjectQuery
            {
                NameQuery = new global::Zitadel.Project.V1.ProjectNameQuery
                {
                    Name = name,
                    Method = ProjectTextQueryMethod.Equals,
                },
            }
        );
        var found = await client.ListProjectsAsync(search, cancellationToken: ct);
        if (found.Result.Count == 0)
        {
            throw new InvalidOperationException(
                $"ZITADEL search for project '{name}' returned no results, but create reported a conflict."
            );
        }
        return found.Result[0].Id;
    }

    // Resolve a possibly-relative path against cwd, then walk upward looking
    // for repo-root markers (pam.slnx or .git). Lets the same config value
    // work whether the API runs from the project dir (dotnet watch) or from
    // the repo root.
    private static string ResolvePatFile(string configured)
    {
        if (Path.IsPathFullyQualified(configured) && File.Exists(configured))
        {
            return configured;
        }

        var fromCwd = Path.GetFullPath(configured);
        if (File.Exists(fromCwd))
        {
            return fromCwd;
        }

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var hasMarker =
                File.Exists(Path.Combine(dir.FullName, "pam.slnx"))
                || Directory.Exists(Path.Combine(dir.FullName, ".git"));
            if (hasMarker)
            {
                var candidate = Path.Combine(dir.FullName, configured);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                break;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"ZITADEL admin PAT file not found. Looked in cwd ({fromCwd}) and walking up for repo root.",
            configured
        );
    }
}
