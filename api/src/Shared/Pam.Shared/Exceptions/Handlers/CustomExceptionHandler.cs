using System.Diagnostics;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Pam.Shared.Exceptions.Handlers;

public sealed class CustomExceptionHandler(ILogger<CustomExceptionHandler> logger)
    : IExceptionHandler
{
    private const string ValidationProblemType =
        "https://tools.ietf.org/html/rfc7231#section-6.5.1";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        // Client disconnect: the request CancellationToken fired because
        // the caller hung up. Not a server-side failure, and we usually
        // can't write a response anyway — the response stream is already
        // gone. Log at debug, mark the connection 499 (nginx convention
        // for "client closed request"), and skip the WriteAsJsonAsync.
        // Without this guard, every k6 ramp-down / client timeout pollutes
        // the error log with stack traces and inflates the 5xx rate.
        if (IsClientCancellation(httpContext, exception))
        {
            logger.LogDebug("Request aborted by client: {Path}", httpContext.Request.Path);
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = 499;
            }
            return true;
        }

        var (status, problem) = exception switch
        {
            // Module-specific domain failures all flow through here —
            // PamDomainException carries Status/Title/Type/Failures, so
            // adding a new exception in a module needs zero handler change.
            PamDomainException pde => MapDomain(pde),
            ValidationException ve => MapValidationFailures(
                ve.Errors,
                StatusCodes.Status400BadRequest,
                title: "Validation failed",
                type: ValidationProblemType,
                detail: "One or more validation errors occurred."
            ),
            UnauthorizedAccessException => MapBare(
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                exception.Message
            ),
            BadHttpRequestException bhre => MapBare(
                bhre.StatusCode,
                "Bad Request",
                BuildBadRequestDetail(bhre)
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

    private static (int Status, ProblemDetails Problem) MapDomain(PamDomainException ex)
    {
        // Failures present → ValidationProblemDetails (errors dict), same
        // shape as FluentValidation 400 but with the exception's own
        // status code. Absent → plain ProblemDetails with the `code`
        // extension clients program against.
        if (ex.Failures is { Count: > 0 } failures)
        {
            var (status, pd) = MapValidationFailures(
                failures,
                ex.Status,
                ex.Title,
                ex.TypeUri ?? ValidationProblemType,
                ex.Message
            );
            pd.Extensions["code"] = ex.Code;
            return (status, pd);
        }

        var problem = new ProblemDetails
        {
            Type = ex.TypeUri,
            Title = ex.Title,
            Status = ex.Status,
            Detail = ex.Message,
        };
        problem.Extensions["code"] = ex.Code;
        return (ex.Status, problem);
    }

    // Shared by the FluentValidation 400 path and any PamDomainException
    // that carries failures so the `errors` dict is grouped identically
    // across surfaces — one client code path consumes both.
    private static (int Status, ProblemDetails Problem) MapValidationFailures(
        IEnumerable<ValidationFailure> failures,
        int status,
        string title,
        string type,
        string detail
    )
    {
        var errors = failures
            .GroupBy(
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
            Type = type,
            Title = title,
            Status = status,
            Detail = detail,
        };
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

    // Minimal APIs wrap JSON parse failures in BadHttpRequestException whose
    // own Message is the generic "Failed to read parameter …". The useful
    // detail (which field, what format) lives in the inner JsonException.
    private static string BuildBadRequestDetail(BadHttpRequestException bhre)
    {
        if (bhre.InnerException is JsonException je)
        {
            var path = string.IsNullOrEmpty(je.Path) ? null : je.Path;
            var reason = je.InnerException?.Message ?? je.Message;
            return path is null ? reason : $"{path}: {reason}";
        }
        return bhre.Message;
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

    // A client cancellation manifests in three flavours:
    //   1. OperationCanceledException / TaskCanceledException directly.
    //   2. DbUpdateException with a SqlException inner whose .Number = 0
    //      and message "Operation cancelled by user." — that's what SQL
    //      Server raises when EF cancels the in-flight command.
    //   3. Either of the above with HttpContext.RequestAborted already
    //      signalled.
    // Any of these means "the client gave up", not "the server broke".
    private static bool IsClientCancellation(HttpContext httpContext, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return true;
        }

        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            // Walk the inner-exception chain — EF wraps SQL cancellation
            // in DbUpdateException, sometimes nested.
            for (var e = exception; e is not null; e = e.InnerException)
            {
                if (e is OperationCanceledException)
                {
                    return true;
                }
                if (
                    e.Message.Contains(
                        "Operation cancelled by user",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }
        }

        return false;
    }
}
