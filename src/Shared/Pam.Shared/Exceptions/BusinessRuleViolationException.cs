namespace Pam.Shared.Exceptions;

public sealed class BusinessRuleViolationException(string code, string message)
    : PamDomainException(code, message);
