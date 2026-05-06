using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pam.Players.Players.Brands;

namespace Pam.Players.Infrastructure.Zitadel;

public sealed class ZitadelBootstrapService(
    IHttpClientFactory httpFactory,
    ZitadelRuntimeState state,
    IOptions<ZitadelOptions> zitadelOpts,
    IOptions<BrandRegistryOptions> brandOpts,
    ILogger<ZitadelBootstrapService> logger
) : IHostedService
{
    private const string OrgIdHeader = "x-zitadel-orgid";
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(2);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var z = zitadelOpts.Value;
        var b = brandOpts.Value;

        await WaitForZitadelReadyAsync(z.Authority, cancellationToken);

        var patPath = ResolvePatFile(z.AdminPatFile);
        state.AdminPat = (await File.ReadAllTextAsync(patPath, cancellationToken)).Trim();
        if (string.IsNullOrEmpty(state.AdminPat))
        {
            throw new InvalidOperationException(
                $"ZITADEL admin PAT file at '{patPath}' is empty."
            );
        }

        using var http = httpFactory.CreateClient("zitadel-bootstrap");
        http.BaseAddress = new Uri(z.ManagementApiBaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            state.AdminPat
        );

        foreach (var brandId in b.BrandIds)
        {
            var orgId = await EnsureOrgAsync(http, brandId, cancellationToken);
            state.BrandOrgIds[brandId] = orgId;
            logger.LogInformation("Brand {BrandId} → ZITADEL Org {OrgId}", brandId, orgId);
        }

        if (!state.BrandOrgIds.TryGetValue(b.Default, out var defaultOrg))
        {
            throw new InvalidOperationException(
                $"Default brand '{b.Default}' is not in Brands:BrandIds."
            );
        }
        state.ProjectId = await EnsureProjectAsync(
            http,
            defaultOrg,
            z.ProjectName,
            cancellationToken
        );
        logger.LogInformation("ZITADEL Project '{Name}' → {ProjectId}", z.ProjectName, state.ProjectId);
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
        HttpClient http,
        string name,
        CancellationToken ct
    )
    {
        using var createResp = await http.PostAsJsonAsync(
            "management/v1/orgs",
            new { name },
            ct
        );
        if (createResp.IsSuccessStatusCode)
        {
            return await ReadIdAsync(createResp, ct);
        }
        if (createResp.StatusCode != HttpStatusCode.Conflict)
        {
            await ThrowFromAsync(createResp, $"create org '{name}'", ct);
        }

        // Already exists — search by name. Org search lives at /v2 in
        // ZITADEL v4.x; create stays at /management/v1.
        using var searchResp = await http.PostAsJsonAsync(
            "v2/organizations/_search",
            new
            {
                queries = new object[]
                {
                    new
                    {
                        nameQuery = new
                        {
                            name,
                            method = "TEXT_QUERY_METHOD_EQUALS",
                        },
                    },
                },
            },
            ct
        );
        searchResp.EnsureSuccessStatusCode();
        return await ReadFirstResultIdAsync(searchResp, $"org '{name}'", ct);
    }

    private static async Task<string> EnsureProjectAsync(
        HttpClient http,
        string orgId,
        string name,
        CancellationToken ct
    )
    {
        using var createReq = new HttpRequestMessage(HttpMethod.Post, "management/v1/projects");
        createReq.Headers.Add(OrgIdHeader, orgId);
        createReq.Content = JsonContent.Create(new { name });
        using var createResp = await http.SendAsync(createReq, ct);
        if (createResp.IsSuccessStatusCode)
        {
            return await ReadIdAsync(createResp, ct);
        }
        if (createResp.StatusCode != HttpStatusCode.Conflict)
        {
            await ThrowFromAsync(createResp, $"create project '{name}'", ct);
        }

        using var searchReq = new HttpRequestMessage(
            HttpMethod.Post,
            "management/v1/projects/_search"
        );
        searchReq.Headers.Add(OrgIdHeader, orgId);
        searchReq.Content = JsonContent.Create(
            new
            {
                queries = new object[]
                {
                    new
                    {
                        nameQuery = new
                        {
                            name,
                            method = "TEXT_QUERY_METHOD_EQUALS",
                        },
                    },
                },
            }
        );
        using var searchResp = await http.SendAsync(searchReq, ct);
        searchResp.EnsureSuccessStatusCode();
        return await ReadFirstResultIdAsync(searchResp, $"project '{name}'", ct);
    }

    private static async Task<string> ReadIdAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return doc.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("ZITADEL response missing 'id' field.");
    }

    private static async Task<string> ReadFirstResultIdAsync(
        HttpResponseMessage resp,
        string what,
        CancellationToken ct
    )
    {
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var result = doc.GetProperty("result");
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"ZITADEL search for {what} returned no results, but create reported a conflict."
            );
        }
        return result[0].GetProperty("id").GetString()
            ?? throw new InvalidOperationException(
                $"ZITADEL search result for {what} missing 'id' field."
            );
    }

    private static async Task ThrowFromAsync(
        HttpResponseMessage resp,
        string action,
        CancellationToken ct
    )
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"ZITADEL {action} failed: {(int)resp.StatusCode} {resp.StatusCode}; body: {body}"
        );
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
