namespace Pam.Shared.Exceptions;

public sealed class AlreadyExistsException(string code, string message)
    : PamDomainException(code, message);
