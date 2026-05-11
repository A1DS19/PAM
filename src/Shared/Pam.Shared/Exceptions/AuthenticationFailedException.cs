using FluentValidation.Results;

namespace Pam.Shared.Exceptions;

// Thrown by auth-flow handlers (login, MFA verify, recovery code, …)
// when the supplied credentials don't match. Carries
// FluentValidation.ValidationFailure entries so CustomExceptionHandler
// can group them the same way it groups LoginValidator's results — the
// HTTP response is a 401 ValidationProblemDetails with the same
// `errors` dict shape clients already handle for 400s.
public sealed class AuthenticationFailedException : Exception
{
    public IReadOnlyCollection<ValidationFailure> Failures { get; }

    public AuthenticationFailedException(string detail, params ValidationFailure[] failures)
        : base(detail)
    {
        Failures = failures.Length == 0 ? [new ValidationFailure("_", detail)] : failures;
    }
}
