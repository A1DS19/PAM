using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.Exceptions;

// 401 with ValidationProblemDetails: same `errors` dict the SPA already
// renders for 400 validation failures, just at a different status code.
// Per-field entries let the client hang inline errors; the wording is
// kept uniform across fields to avoid being a user-enumeration oracle.
public sealed class AuthenticationFailedException : PamDomainException
{
    public AuthenticationFailedException(
        string code,
        string detail,
        params ValidationFailure[] failures
    )
        : base(
            code: code,
            message: detail,
            status: StatusCodes.Status401Unauthorized,
            title: "Unauthorized",
            typeUri: "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            failures: failures.Length == 0 ? [new ValidationFailure("_", detail)] : failures
        ) { }
}
