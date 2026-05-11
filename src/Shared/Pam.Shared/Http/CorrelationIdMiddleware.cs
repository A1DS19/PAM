using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pam.Shared.Http;

// Threads an `X-Correlation-Id` header through the request: read on the
// way in (generate if absent), echo on the way out, push it into the
// current Activity's baggage so MassTransit + outbox carry it through
// to async consumers, and into the logger scope so every log line in
// this request includes it.
//
// OTel's `traceparent` propagates a synchronous HTTP call chain on its
// own. CorrelationId fills the gap for *async* flows: the outbox writes
// the message hours later, the consumer runs in a different process,
// and the original W3C trace is long gone. The correlation id stays.
//
// Format is a 32-char `N`-format GUID — short, opaque, sortable when
// generated from `Guid.CreateVersion7`, but accept any non-empty input
// the caller sends so external systems can stitch their own id through.
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger
)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string BaggageKey = "correlation.id";
    public const string LogScopeKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.CreateVersion7().ToString("N");
        }

        context.Response.Headers[HeaderName] = id;

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.AddBaggage(BaggageKey, id);
            activity.SetTag(BaggageKey, id);
        }

        var scope = new Dictionary<string, object>(StringComparer.Ordinal) { [LogScopeKey] = id };
        using (logger.BeginScope(scope))
        {
            await next(context);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
