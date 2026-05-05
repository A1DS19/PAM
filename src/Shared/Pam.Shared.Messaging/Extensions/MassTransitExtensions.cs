using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pam.Shared.Messaging.Extensions;

public static class MassTransitExtensions
{
    public static IServiceCollection AddPamMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] consumerAssemblies
    )
    {
        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            if (consumerAssemblies.Length > 0)
            {
                x.AddConsumers(consumerAssemblies);
            }

            x.UsingRabbitMq(
                (context, cfg) =>
                {
                    var section = configuration.GetSection("MessageBroker");
                    var host =
                        section["Host"]
                        ?? throw new InvalidOperationException("MessageBroker:Host is required");

                    cfg.Host(
                        host,
                        section["VirtualHost"] ?? "/",
                        h =>
                        {
                            h.Username(section["Username"] ?? "guest");
                            h.Password(section["Password"] ?? "guest");
                        }
                    );

                    cfg.ConfigureEndpoints(context);
                }
            );
        });

        return services;
    }
}
