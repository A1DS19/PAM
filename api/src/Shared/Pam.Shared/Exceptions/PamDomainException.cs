using System.Diagnostics.CodeAnalysis;
using FluentValidation.Results;

namespace Pam.Shared.Exceptions;

// Base for every domain exception a handler throws to surface as a
// ProblemDetails response. Subclasses live in the module they belong to
// (e.g. AuthenticationFailedException in Pam.Identity); each one bakes
// in the HTTP shape (Status, Title, optional Type, optional Failures)
// so CustomExceptionHandler can render it without a per-type switch arm.
public abstract class PamDomainException : Exception
{
    [SuppressMessage(
        "Design",
        "CA1054:URI parameters should not be strings",
        Justification = "Mirrors ProblemDetails.Type which is a string per RFC 7807."
    )]
    protected PamDomainException(
        string code,
        string message,
        int status,
        string title,
        string? typeUri = null,
        IReadOnlyCollection<ValidationFailure>? failures = null
    )
        : base(message)
    {
        Code = code;
        Status = status;
        Title = title;
        TypeUri = typeUri;
        Failures = failures;
    }

    public string Code { get; }
    public int Status { get; }
    public string Title { get; }
    public string? TypeUri { get; }

    // When non-empty, CustomExceptionHandler renders as ValidationProblemDetails
    // (same `errors` dict shape as FluentValidation 400s). Otherwise plain
    // ProblemDetails. Lets a 401 carry per-field hints without a parallel
    // exception type.
    public IReadOnlyCollection<ValidationFailure>? Failures { get; }
}
