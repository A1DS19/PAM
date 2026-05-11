using System.Reflection;
using MediatR;
using Microsoft.Extensions.Logging;
using Pam.Shared.Contracts.Caching;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Shared.Behaviors;

public sealed class CachingBehavior<TRequest, TResponse>(
    ICacheService cacheService,
    ILogger<CachingBehavior<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        // Commands never read the cache — they only invalidate after they succeed.
        if (request is not IQuery<TResponse>)
        {
            var commandResponse = await next();
            await InvalidateCacheIfNeededAsync(cancellationToken);
            return commandResponse;
        }

        var cacheAttribute = typeof(TRequest).GetCustomAttribute<CacheAttribute>();
        if (cacheAttribute is null)
        {
            return await next();
        }

        var cacheKey = CacheKeyGenerator.GenerateKey(request, cacheAttribute.KeyPattern);

        var cachedResponse = await cacheService.GetAsync<TResponse>(cacheKey, cancellationToken);
        if (cachedResponse is not null)
        {
            logger.LogInformation("Cache hit for key: {CacheKey}", cacheKey);
            return cachedResponse;
        }

        logger.LogInformation("Cache miss for key: {CacheKey}", cacheKey);
        var response = await next();

        await cacheService.SetAsync(cacheKey, response, cacheAttribute.Duration, cancellationToken);
        logger.LogInformation(
            "Cached response for key: {CacheKey} with expiration: {Duration}",
            cacheKey,
            cacheAttribute.Duration
        );

        return response;
    }

    private async Task InvalidateCacheIfNeededAsync(CancellationToken cancellationToken)
    {
        var invalidateAttribute = typeof(TRequest).GetCustomAttribute<InvalidateCacheAttribute>();
        if (invalidateAttribute is null)
        {
            return;
        }

        foreach (var pattern in invalidateAttribute.Patterns)
        {
            try
            {
                await cacheService.RemoveByPatternAsync(pattern, cancellationToken);
                logger.LogInformation("Invalidated cache with pattern: {Pattern}", pattern);
            }
            catch (Exception ex)
            {
                // Invalidation failure must never fail the command — the
                // write already succeeded; stale cache is the lesser evil.
                logger.LogWarning(
                    ex,
                    "Failed to invalidate cache with pattern: {Pattern}",
                    pattern
                );
            }
        }
    }
}
