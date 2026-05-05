namespace Pam.Shared.Exceptions;

public abstract class PamDomainException : Exception
{
    protected PamDomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
