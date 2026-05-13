using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.Mfa;

// Generates an authenticator key on first call; reuses the existing key on
// subsequent calls until the user verifies it. After verification the user
// has to disable + re-enroll to rotate the secret.
//
// The otpauth:// URI is the standard format (RFC 6238 + Google's extension):
//   otpauth://totp/PAM:{email}?secret={base32}&issuer=PAM&algorithm=SHA1&digits=6&period=30
public sealed class MfaEnrollHandler(
    UserManager<BackOfficeUser> userManager,
    IHttpContextAccessor httpContext
) : ICommandHandler<MfaEnrollCommand, MfaEnrollResult>
{
    private const string Issuer = "PAM";

    public async Task<MfaEnrollResult> Handle(
        MfaEnrollCommand command,
        CancellationToken cancellationToken
    )
    {
        var principal =
            httpContext.HttpContext?.User
            ?? throw new UnauthorizedAccessException("No HTTP context.");
        var sub =
            principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
        {
            throw new UnauthorizedAccessException("Caller has no sub claim.");
        }

        var user =
            await userManager.FindByIdAsync(sub)
            ?? throw new UnauthorizedAccessException("Authenticated user no longer exists.");

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            var resetResult = await userManager.ResetAuthenticatorKeyAsync(user);
            if (!resetResult.Succeeded)
            {
                throw new BusinessRuleViolationException(
                    UserErrors.MfaEnrollFailed,
                    string.Join("; ", resetResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
                );
            }
            key =
                await userManager.GetAuthenticatorKeyAsync(user)
                ?? throw new InvalidOperationException(
                    "GetAuthenticatorKeyAsync returned null after a successful reset."
                );
        }

        var email = await userManager.GetEmailAsync(user) ?? user.UserName ?? "user";
        var uri = new Uri(
            string.Format(
                CultureInfo.InvariantCulture,
                "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&algorithm=SHA1&digits=6&period=30",
                UrlEncoder.Default.Encode(Issuer),
                UrlEncoder.Default.Encode(email),
                key
            )
        );

        return new MfaEnrollResult(FormatKey(key), uri);
    }

    // Groups the base32 key into 4-char chunks for readability when the user
    // has to type it manually into an authenticator that can't scan QR codes.
    private static string FormatKey(string key)
    {
        var sb = new StringBuilder(key.Length + key.Length / 4);
        for (var i = 0; i < key.Length; i += 4)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(key, i, Math.Min(4, key.Length - i));
        }
        return sb.ToString().ToLowerInvariant();
    }
}
