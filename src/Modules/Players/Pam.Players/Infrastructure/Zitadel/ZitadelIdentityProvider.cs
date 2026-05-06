using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pam.Players.Players.Brands;
using Pam.Players.Players.Exceptions;
using Pam.Players.Players.Identity;
using Pam.Shared.Exceptions;

namespace Pam.Players.Infrastructure.Zitadel;

public sealed class ZitadelIdentityProvider(
    HttpClient http,
    IBrandRegistry brands,
    ILogger<ZitadelIdentityProvider> logger
) : IIdentityProvider
{
    private const string OrgIdHeader = "x-zitadel-orgid";

    public async Task<IdentityUserId> CreateUserAsync(
        CreateIdentityUser input,
        CancellationToken ct
    )
    {
        var orgId = brands.GetOrgId(input.BrandId);

        var payload = new
        {
            userName = input.Email,
            profile = new
            {
                firstName = input.FirstName,
                lastName = input.LastName,
                displayName = $"{input.FirstName} {input.LastName}".Trim(),
                preferredLanguage = "en",
            },
            email = new { email = input.Email, isEmailVerified = !input.RequireEmailVerify },
            initialPassword = input.Password,
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "management/v1/users/human/_import"
        );
        req.Headers.Add(OrgIdHeader, orgId);
        req.Content = JsonContent.Create(payload);

        using var resp = await http.SendAsync(req, ct);

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
            logger.LogError("ZITADEL create user failed: {Status} {Body}", resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userId =
            doc.GetProperty("userId").GetString()
            ?? throw new InvalidOperationException("ZITADEL did not return userId on create.");

        return new IdentityUserId(userId);
    }

    public async Task SendVerifyEmailAsync(IdentityUserId id, CancellationToken ct)
    {
        using var content = JsonContent.Create(new { });
        using var resp = await http.PostAsync(
            new Uri($"management/v1/users/{id.Value}/email/_resendverification", UriKind.Relative),
            content,
            ct
        );
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "ZITADEL resend verify-email returned {Status} for user {UserId}",
                resp.StatusCode,
                id.Value
            );
        }
    }

    public async Task DeleteUserAsync(IdentityUserId id, CancellationToken ct)
    {
        using var resp = await http.DeleteAsync(
            new Uri($"management/v1/users/{id.Value}", UriKind.Relative),
            ct
        );
        if (resp.StatusCode != HttpStatusCode.NotFound)
        {
            resp.EnsureSuccessStatusCode();
        }
    }
}
