using Microsoft.AspNetCore.Http;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.Exceptions;

// 423 Locked, plain ProblemDetails — lockout isn't field-shaped, so it
// stays a top-level "you can't try again right now" signal.
public sealed class AccountLockedException(string code, string message)
    : PamDomainException(code, message, StatusCodes.Status423Locked, "Locked");
