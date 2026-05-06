using Grpc.Core;
using Microsoft.Extensions.Logging;
using Pam.Players.Players.Brands;
using Pam.Players.Players.Exceptions;
using Pam.Players.Players.Identity;
using Pam.Shared.Exceptions;
using Zitadel.Management.V1;

namespace Pam.Players.Infrastructure.Zitadel;

public sealed class ZitadelIdentityProvider(
    ZitadelClientFactory clients,
    IBrandRegistry brands,
    ILogger<ZitadelIdentityProvider> logger
) : IIdentityProvider
{
    public async Task<IdentityUserId> CreateUserAsync(
        CreateIdentityUser input,
        CancellationToken ct
    )
    {
        var orgId = brands.GetOrgId(input.BrandId);
        var mgmt = clients.ManagementClient(orgId);

        var req = new ImportHumanUserRequest
        {
            UserName = input.Email,
            Profile = new ImportHumanUserRequest.Types.Profile
            {
                FirstName = input.FirstName,
                LastName = input.LastName,
                DisplayName = $"{input.FirstName} {input.LastName}".Trim(),
                PreferredLanguage = "en",
            },
            Email = new ImportHumanUserRequest.Types.Email
            {
                Email_ = input.Email,
                IsEmailVerified = !input.RequireEmailVerify,
            },
            Password = input.Password,
        };

        try
        {
            var resp = await mgmt.ImportHumanUserAsync(req, cancellationToken: ct);
            return new IdentityUserId(resp.UserId);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            throw new AlreadyExistsException(
                PlayerErrors.EmailAlreadyRegistered,
                "An account with this email already exists."
            );
        }
        catch (RpcException ex)
        {
            logger.LogError(
                ex,
                "ZITADEL ImportHumanUser failed: {Status} {Detail}",
                ex.StatusCode,
                ex.Status.Detail
            );
            throw;
        }
    }

    public async Task SendVerifyEmailAsync(string brandId, IdentityUserId id, CancellationToken ct)
    {
        // ImportHumanUser sets a password, so the user needs verify-email,
        // not the initialization flow (which is for password-less imports).
        // ZITADEL's management API is org-scoped — without the org header it
        // returns NotFound for users that exist in a different org context.
        var orgId = brands.GetOrgId(brandId);
        var mgmt = clients.ManagementClient(orgId);
        try
        {
            await mgmt.ResendHumanEmailVerificationAsync(
                new ResendHumanEmailVerificationRequest { UserId = id.Value },
                cancellationToken: ct
            );
        }
        catch (RpcException ex)
        {
            logger.LogWarning(
                ex,
                "ZITADEL ResendHumanEmailVerification returned {Status} for user {UserId}",
                ex.StatusCode,
                id.Value
            );
        }
    }

    public async Task DeleteUserAsync(string brandId, IdentityUserId id, CancellationToken ct)
    {
        var orgId = brands.GetOrgId(brandId);
        var mgmt = clients.ManagementClient(orgId);
        try
        {
            await mgmt.RemoveUserAsync(
                new RemoveUserRequest { Id = id.Value },
                cancellationToken: ct
            );
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // Idempotent delete: user already gone.
        }
    }
}
