using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Pam.Shared.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName}", requestName);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            sw.Stop();
            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs}ms",
                requestName,
                sw.ElapsedMilliseconds
            );
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(
                ex,
                "Failed {RequestName} after {ElapsedMs}ms: {Message}",
                requestName,
                sw.ElapsedMilliseconds,
                ex.Message
            );
            throw;
        }
    }
}
