using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pam.Players.Infrastructure.Keycloak;

public sealed class AdminTokenHandler(
    IOptions<KeycloakOptions> options,
    IHttpClientFactory factory,
    ILogger<AdminTokenHandler> logger
) : DelegatingHandler
{
    private readonly KeycloakOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiry;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var token = await GetOrRefreshTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetOrRefreshTokenAsync(CancellationToken ct)
    {
        if (_token is not null && _expiry > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _token;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && _expiry > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return _token;
            }
            var (token, expiresIn) = await FetchTokenAsync(ct);
            _token = token;
            _expiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<(string Token, int ExpiresIn)> FetchTokenAsync(CancellationToken ct)
    {
        using var http = factory.CreateClient("keycloak-token");
        var url =
            $"{_options.AuthServerUrl.TrimEnd('/')}/realms/{_options.AdminRealm}/protocol/openid-connect/token";

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = _options.AdminClientId,
        };

        if (!string.IsNullOrEmpty(_options.AdminClientSecret))
        {
            form["grant_type"] = "client_credentials";
            form["client_secret"] = _options.AdminClientSecret!;
        }
        else if (
            !string.IsNullOrEmpty(_options.AdminUsername)
            && !string.IsNullOrEmpty(_options.AdminPassword)
        )
        {
            form["grant_type"] = "password";
            form["username"] = _options.AdminUsername!;
            form["password"] = _options.AdminPassword!;
        }
        else
        {
            throw new InvalidOperationException(
                "Keycloak admin credentials not configured. Set AdminClientSecret or AdminUsername+AdminPassword."
            );
        }

        using var content = new FormUrlEncodedContent(form);
        using var resp = await http.PostAsync(new Uri(url), content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Keycloak admin token request failed: {Status} {Body}",
                resp.StatusCode,
                body
            );
            resp.EnsureSuccessStatusCode();
        }

        var tokenResp =
            await resp.Content.ReadFromJsonAsync<TokenResponse>(ct)
            ?? throw new InvalidOperationException("Keycloak token response was empty.");
        return (tokenResp.AccessToken, tokenResp.ExpiresIn);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn
    );

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lock.Dispose();
        }
        base.Dispose(disposing);
    }
}
