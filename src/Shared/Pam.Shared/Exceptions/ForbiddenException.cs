using Microsoft.AspNetCore.Http;

namespace Pam.Shared.Exceptions;

public sealed class ForbiddenException(string code, string message)
    : PamDomainException(code, message, StatusCodes.Status403Forbidden, "Forbidden");
