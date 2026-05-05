using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Pam.Api.Infrastructure.Infisical;

public sealed class InfisicalSecretsConfigurationProvider(InfisicalOptions options)
    : ConfigurationProvider
{
    public override void Load()
    {
        if (!options.IsConfigured)
        {
            if (options.Optional)
            {
                return;
            }
            throw new InvalidOperationException(
                "Infisical configuration is incomplete (Host, ProjectId, ClientId, ClientSecret all required) and Optional=false."
            );
        }

        try
        {
            LoadAsync().GetAwaiter().GetResult();
        }
        catch when (options.Optional)
        {
            // Swallow — fall back to other configuration sources.
        }
    }

    private async Task LoadAsync()
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(options.Host!.TrimEnd('/') + "/"),
            Timeout = options.Timeout,
        };

        var loginResp = await http.PostAsJsonAsync(
            "api/v1/auth/universal-auth/login",
            new { clientId = options.ClientId, clientSecret = options.ClientSecret }
        );
        loginResp.EnsureSuccessStatusCode();

        var login =
            await loginResp.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Infisical login returned empty body.");

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        var url =
            $"api/v3/secrets/raw"
            + $"?workspaceId={Uri.EscapeDataString(options.ProjectId!)}"
            + $"&environment={Uri.EscapeDataString(options.Environment ?? "dev")}"
            + $"&secretPath={Uri.EscapeDataString(options.SecretPath ?? "/")}"
            + $"&recursive=true";

        var secretsResp = await http.GetAsync(new Uri(url, UriKind.Relative));
        secretsResp.EnsureSuccessStatusCode();

        var payload = await secretsResp.Content.ReadFromJsonAsync<SecretsResponse>();
        if (payload?.Secrets is null)
        {
            return;
        }

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in payload.Secrets)
        {
            // Infisical secret keys use `__` as the nesting separator (env-var convention).
            // ASP.NET Core configuration uses `:`. Translate.
            var key = s.SecretKey.Replace("__", ":", StringComparison.Ordinal);
            data[key] = s.SecretValue;
        }
        Data = data;
    }

    private sealed record LoginResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn
    );

    private sealed record SecretsResponse(
        [property: JsonPropertyName("secrets")] List<Secret>? Secrets
    );

    private sealed record Secret(
        [property: JsonPropertyName("secretKey")] string SecretKey,
        [property: JsonPropertyName("secretValue")] string SecretValue
    );
}
