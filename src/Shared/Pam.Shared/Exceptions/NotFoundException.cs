namespace Pam.Shared.Exceptions;

public sealed class NotFoundException(string code, string message)
    : PamDomainException(code, message);
