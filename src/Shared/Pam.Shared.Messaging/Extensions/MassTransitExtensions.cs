using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pam.Shared.Messaging.Extensions;

public static class MassTransitExtensions
{
    // `configureBus` is the extension point per publishing module — each
    // module that needs an EF Core outbox calls
    // `x.AddEntityFrameworkOutbox<TContext>(...)` here. The shared bus
    // registration lives in this method (broker config, consumers,
    // endpoint conventions); module-specific outbox wiring stays in the
    // module so adding a new publisher doesn't require touching shared
    // code. Callsites in Program.cs compose the configure delegates.
    public static IServiceCollection AddPamMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureBus = null,
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

            configureBus?.Invoke(x);

            x.UsingRabbitMq(
                (context, cfg) =>
                {
                    var section = configuration.GetSection("MessageBroker");
                    var host =
                        section["Host"]
                        ?? throw new InvalidOperationException("MessageBroker:Host is required");
                    var vhost = section["VirtualHost"] ?? "/";

                    // Port is optional — local dev uses RabbitMQ's default 5672
                    // and skips this key. Testcontainers (and any non-default
                    // setup) maps to a host-side ephemeral port and sets it.
                    if (
                        ushort.TryParse(
                            section["Port"],
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var port
                        )
                    )
                    {
                        cfg.Host(
                            host,
                            port,
                            vhost,
                            h =>
                            {
                                h.Username(section["Username"] ?? "guest");
                                h.Password(section["Password"] ?? "guest");
                            }
                        );
                    }
                    else
                    {
                        cfg.Host(
                            host,
                            vhost,
                            h =>
                            {
                                h.Username(section["Username"] ?? "guest");
                                h.Password(section["Password"] ?? "guest");
                            }
                        );
                    }

                    cfg.ConfigureEndpoints(context);
                }
            );
        });

        return services;
    }
}
