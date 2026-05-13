using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Pam.Shared.Audit;
using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Http;
using Pam.Shared.Security;
using Pam.Shared.Time;

namespace Pam.Shared.Behaviors;

public sealed class AuditBehavior<TRequest, TResponse>(
    IAuditService auditService,
    IUserContext userContext,
    IClock clock,
    ILogger<AuditBehavior<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        // Commands only — queries are out of scope (DECISIONS.md #22).
        if (!IsCommand(request))
        {
            return await next();
        }

        var startedAt = clock.UtcNow;
        try
        {
            var response = await next();
            await RecordAsync(
                request,
                startedAt,
                AuditStatus.Success,
                errorType: null,
                errorMessage: null,
                cancellationToken
            );
            return response;
        }
        catch (Exception ex)
        {
            await RecordAsync(
                request,
                startedAt,
                AuditStatus.Failure,
                errorType: ex.GetType().FullName,
                errorMessage: ex.Message,
                CancellationToken.None
            );
            throw;
        }
    }

    private static bool IsCommand(TRequest request) =>
        request is ICommand
        || request
            .GetType()
            .GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    private async Task RecordAsync(
        TRequest request,
        DateTimeOffset startedAt,
        AuditStatus status,
        string? errorType,
        string? errorMessage,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var entry = new AuditEntry(
                CorrelationId: Activity.Current?.GetBaggageItem(CorrelationIdMiddleware.BaggageKey),
                ActorType: userContext.Current.Type,
                ActorId: userContext.Current.Id,
                RequestType: typeof(TRequest).FullName ?? typeof(TRequest).Name,
                PayloadJson: SensitiveJsonRedactor.Serialize(request),
                StartedAt: startedAt,
                CompletedAt: clock.UtcNow,
                Status: status,
                ErrorType: errorType,
                ErrorMessage: Truncate(errorMessage, 1024)
            );

            await auditService.RecordAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            // Belt-and-braces: AuditService already swallows its own
            // errors, but anything that escapes (e.g. redactor throws on
            // an unexpected payload) must not propagate and fail the
            // command path.
            logger.LogWarning(
                ex,
                "Failed to build audit entry for {RequestType}",
                typeof(TRequest).FullName
            );
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null)
        {
            return null;
        }
        return value.Length <= max ? value : value[..max];
    }
}
