using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Pam.Shared.Exceptions.Handlers;

public sealed class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var (status, problem) = exception switch
        {
            ValidationException ve => MapValidation(ve),
            NotFoundException nfe => Map(nfe, StatusCodes.Status404NotFound, "Not Found"),
            AlreadyExistsException ae => Map(ae, StatusCodes.Status409Conflict, "Already Exists"),
            BusinessRuleViolationException be => Map(
                be,
                StatusCodes.Status422UnprocessableEntity,
                "Business Rule Violation"
            ),
            ForbiddenException fe => Map(fe, StatusCodes.Status403Forbidden, "Forbidden"),
            UnauthorizedAccessException => MapBare(
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                exception.Message
            ),
            _ => MapInternal(),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception");
        }
        else
        {
            logger.LogWarning(
                exception,
                "Handled domain exception: {Type}",
                exception.GetType().Name
            );
        }

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            problem.GetType(),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken
        );
        return true;
    }

    private static (int Status, ProblemDetails Problem) MapValidation(ValidationException ve)
    {
        var errors = ve
            .Errors.GroupBy(
                e => string.IsNullOrEmpty(e.PropertyName) ? "_" : e.PropertyName,
                StringComparer.Ordinal
            )
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray(),
                StringComparer.Ordinal
            );

        var pd = new ValidationProblemDetails(errors)
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest,
            Detail = "One or more validation errors occurred.",
        };
        return (StatusCodes.Status400BadRequest, pd);
    }

    private static (int Status, ProblemDetails Problem) Map(
        PamDomainException ex,
        int status,
        string title
    )
    {
        var pd = new ProblemDetails
        {
            Title = title,
            Status = status,
            Detail = ex.Message,
        };
        pd.Extensions["code"] = ex.Code;
        return (status, pd);
    }

    private static (int Status, ProblemDetails Problem) MapBare(
        int status,
        string title,
        string detail
    )
    {
        return (
            status,
            new ProblemDetails
            {
                Title = title,
                Status = status,
                Detail = detail,
            }
        );
    }

    private static (int Status, ProblemDetails Problem) MapInternal()
    {
        return (
            StatusCodes.Status500InternalServerError,
            new ProblemDetails
            {
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred.",
            }
        );
    }
}
