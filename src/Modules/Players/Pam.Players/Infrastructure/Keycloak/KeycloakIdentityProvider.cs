using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pam.Players.Players.Exceptions;
using Pam.Players.Players.Identity;
using Pam.Shared.Exceptions;

namespace Pam.Players.Infrastructure.Keycloak;

public sealed class KeycloakIdentityProvider(
    HttpClient adminHttp,
    IOptions<KeycloakOptions> options,
    ILogger<KeycloakIdentityProvider> logger
) : IIdentityProvider
{
    private readonly KeycloakOptions _options = options.Value;

    public async Task<IdentityUserId> CreateUserAsync(
        CreateIdentityUser input,
        CancellationToken ct
    )
    {
        var realm = _options.PlayersRealm;
        var attributesPayload = input.Attributes?.ToDictionary(
            kv => kv.Key,
            kv => new[] { kv.Value },
            StringComparer.Ordinal);
        var payload = new
        {
            email = input.Email,
            username = input.Email,
            firstName = input.FirstName,
            lastName = input.LastName,
            enabled = true,
            emailVerified = !input.RequireEmailVerify,
            credentials = new[]
            {
                new
                {
                    type = "password",
                    value = input.Password,
                    temporary = false,
                },
            },
            attributes = attributesPayload,
        };

        using var resp = await adminHttp.PostAsJsonAsync(
            $"admin/realms/{realm}/users",
            payload,
            ct
        );

        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            throw new AlreadyExistsException(
                PlayerErrors.EmailAlreadyRegistered,
                "An account with this email already exists."
            );
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogError("Keycloak create user failed: {Status} {Body}", resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }

        var location =
            resp.Headers.Location?.ToString()
            ?? throw new InvalidOperationException(
                "Keycloak did not return Location header on user create."
            );
        var id = location[(location.LastIndexOf('/') + 1)..];
        return new IdentityUserId(id);
    }

    public async Task SetUserAttributesAsync(
        IdentityUserId id,
        IDictionary<string, string> attributes,
        CancellationToken ct
    )
    {
        var realm = _options.PlayersRealm;
        var attrPayload = attributes.ToDictionary(
            kv => kv.Key,
            kv => new[] { kv.Value },
            StringComparer.Ordinal
        );
        var payload = new { attributes = attrPayload };
        using var resp = await adminHttp.PutAsJsonAsync(
            $"admin/realms/{realm}/users/{id.Value}",
            payload,
            ct
        );
        resp.EnsureSuccessStatusCode();
    }

    public async Task SendVerifyEmailAsync(IdentityUserId id, CancellationToken ct)
    {
        var realm = _options.PlayersRealm;
        using var content = new StringContent(
            "[\"VERIFY_EMAIL\"]",
            Encoding.UTF8,
            "application/json"
        );
        using var resp = await adminHttp.PutAsync(
            new Uri(
                $"admin/realms/{realm}/users/{id.Value}/execute-actions-email",
                UriKind.Relative
            ),
            content,
            ct
        );
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Keycloak execute-actions-email returned {Status} for user {UserId}",
                resp.StatusCode,
                id.Value
            );
        }
    }

    public async Task DeleteUserAsync(IdentityUserId id, CancellationToken ct)
    {
        var realm = _options.PlayersRealm;
        using var resp = await adminHttp.DeleteAsync(
            new Uri($"admin/realms/{realm}/users/{id.Value}", UriKind.Relative),
            ct
        );
        if (resp.StatusCode != HttpStatusCode.NotFound)
        {
            resp.EnsureSuccessStatusCode();
        }
    }
}
