namespace Pam.Shared.Exceptions;

public sealed class AccountLockedException(string code, string message)
    : PamDomainException(code, message);
