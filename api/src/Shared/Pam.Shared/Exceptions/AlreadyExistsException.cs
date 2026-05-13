using Microsoft.AspNetCore.Http;

namespace Pam.Shared.Exceptions;

public sealed class AlreadyExistsException(string code, string message)
    : PamDomainException(code, message, StatusCodes.Status409Conflict, "Already Exists");
