using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pam.Shared.Behaviors;
using Pam.Shared.Caching;
using Pam.Shared.Contracts.Caching;
using Pam.Shared.Security;
using Pam.Shared.Time;

namespace Pam.Shared.Extensions;

public static class SharedServiceCollectionExtensions
{
    public static IServiceCollection AddPamShared(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.TryAddScopedUserContext();
        return services;
    }

    public static IServiceCollection AddPamMediatR(
        this IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(assemblies);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            // Caching runs after validation so invalid requests never poison
            // the cache, and so cache hits/misses still show up in log timings.
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));
        });

        services.AddValidatorsFromAssemblies(assemblies);
        return services;
    }

    public static IServiceCollection AddPamCaching(this IServiceCollection services)
    {
        services.AddSingleton<ICacheService, RedisCacheService>();
        return services;
    }

    private static void TryAddScopedUserContext(this IServiceCollection services)
    {
        if (services.Any(s => s.ServiceType == typeof(IUserContext)))
        {
            return;
        }
        services.AddScoped<IUserContext, SystemUserContext>();
    }
}
