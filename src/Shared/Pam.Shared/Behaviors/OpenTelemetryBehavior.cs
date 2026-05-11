using System.Diagnostics;
using MediatR;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Http;
using Pam.Shared.Observability;
using Pam.Shared.Security;

namespace Pam.Shared.Behaviors;

public sealed class OpenTelemetryBehavior<TRequest, TResponse>(IUserContext userContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        var requestTypeName = typeof(TRequest).Name;
        using var activity = PamActivitySources.MediatR.StartActivity(
            $"mediatr {requestTypeName}",
            ActivityKind.Internal
        );

        // No subscriber listening (e.g. local dev with OTLP disabled) —
        // skip the tag work, just run the handler.
        if (activity is null)
        {
            return await next();
        }

        var actor = userContext.Current;
        activity.SetTag("mediatr.request_type", typeof(TRequest).FullName);
        activity.SetTag("mediatr.request_kind", RequestKind(request));
        activity.SetTag("actor.type", actor.Type.ToString());
        activity.SetTag("actor.id", actor.Id);

        var correlationId = Activity.Current?.GetBaggageItem(CorrelationIdMiddleware.BaggageKey);
        if (correlationId is not null)
        {
            activity.SetTag("correlation.id", correlationId);
        }

        try
        {
            var response = await next();
            activity.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddException(ex);
            throw;
        }
    }

    private static string RequestKind(TRequest request) =>
        request switch
        {
            ICommand => "command",
            _ when request.GetType().GetInterfaces().Any(IsGenericCommand) => "command",
            _ when request.GetType().GetInterfaces().Any(IsGenericQuery) => "query",
            _ => "request",
        };

    private static bool IsGenericCommand(Type i) =>
        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>);

    private static bool IsGenericQuery(Type i) =>
        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>);
}
