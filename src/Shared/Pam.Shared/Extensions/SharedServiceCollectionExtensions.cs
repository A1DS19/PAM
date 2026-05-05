using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Pam.Shared.Behaviors;
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
        });

        services.AddValidatorsFromAssemblies(assemblies);
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
