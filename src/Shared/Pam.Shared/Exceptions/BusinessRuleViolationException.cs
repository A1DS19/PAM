using Microsoft.AspNetCore.Http;

namespace Pam.Shared.Exceptions;

public sealed class BusinessRuleViolationException(string code, string message)
    : PamDomainException(
        code,
        message,
        StatusCodes.Status422UnprocessableEntity,
        "Business Rule Violation"
    );
