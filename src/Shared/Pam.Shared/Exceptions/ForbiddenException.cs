namespace Pam.Shared.Exceptions;

public sealed class ForbiddenException(string code, string message)
    : PamDomainException(code, message);
