using System.Reflection;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pam.Shared.Messaging.Behaviors;
using Pam.Shared.Messaging.Data;
using Pam.Shared.Messaging.Reconciliation;

namespace Pam.Shared.Messaging.Extensions;

public static class MassTransitExtensions
{
    // Wires the entire messaging stack: PamMessagingDbContext (single home
    // for inbox_state/outbox_state/outbox_message), the bus-wide outbox via
    // .UseBusOutbox() on that one DbContext, the per-command flush
    // pipeline behavior, and the RabbitMQ transport.
    //
    // No per-module ConfigureOutbox delegates — see the comment on
    // PamMessagingDbContext for the architectural rationale (and
    // docs/DECISIONS.md ADR on outbox topology).
    //
    // Module DbContexts no longer carry inbox/outbox entities; the
    // RemoveOutboxTables migration in each module drops the per-module
    // tables.
    public static IServiceCollection AddPamMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<Type, bool>? consumerFilter,
        params Assembly[] consumerAssemblies
    )
    {
        var connectionString =
            configuration.GetConnectionString("Pam")
            ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured");

        // Shared outbox/inbox DbContext. Lives in schema "messaging" with
        // its own migrations history. Saved by OutboxFlushBehavior at the
        // tail of every command pipeline.
        services.AddDbContext<PamMessagingDbContext>(
            (sp, options) =>
            {
                options.UseSqlServer(
                    connectionString,
                    sql =>
                    {
                        sql.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            PamMessagingDbContext.Schema
                        );
                        sql.MigrationsAssembly(typeof(PamMessagingDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        // Pipeline behavior runs after every ICommand handler — flushes
        // the change-tracker outbox rows that MT's UseBusOutbox accumulated
        // during the handler's SaveChanges. Registered AFTER AddPamMediatR
        // (which Program.cs always calls first) so it sits innermost in
        // the MediatR pipeline, closest to the handler.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxFlushBehavior<,>));

        // Reconciler backstop. Each module that publishes integration
        // events registers an IOutboxReconciler in its AddXModule.
        // OutboxReconciliationService iterates them on the configured
        // Interval; orphan business rows (no matching dispatched_log row)
        // get republished. See DECISIONS.md ADR #28.
        //
        // OutboxReconciliationOptions is bound in Pam.Api's Program.cs
        // (which has the framework binder available) — defaults apply
        // here if no configuration is bound by the host.
        services.AddOptions<OutboxReconciliationOptions>();
        services.AddHostedService<OutboxReconciliationService>();

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            if (consumerAssemblies.Length > 0)
            {
                if (consumerFilter is null)
                {
                    x.AddConsumers(consumerAssemblies);
                }
                else
                {
                    // MT applies the filter at registration time — types
                    // returning false are not registered at all, so no
                    // receive endpoint or queue gets declared for them.
                    // This is how stress-only discard consumers stay out
                    // of dev/prod wire-up.
                    x.AddConsumers(consumerFilter, consumerAssemblies);
                }
            }

            // Single bus-wide outbox registration on the shared messaging
            // context. UseBusOutbox installs the publish-intercepting bus
            // context provider as IScopedBusContextProvider<IBus> — keyed
            // on the bus type, so MT 8.5.x can attach only one DbContext
            // here. Calling this from more than one module would silently
            // overwrite this registration (last writer wins on a per-bus
            // singleton — verified by reflection 2026-05-12).
            //
            // BusOutboxDeliveryService<PamMessagingDbContext> is auto-
            // registered as a hosted service and polls messaging.outbox_state
            // every QueryDelay (active sends wake it via BusOutboxNotification).
            x.AddEntityFrameworkOutbox<PamMessagingDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox(bo =>
                {
                    // MessageDeliveryLimit caps the rows the delivery
                    // service pulls per iteration. Default is 100, which
                    // tops out around 600-700 msg/sec on local hardware
                    // — fine for current dev load, but observed in
                    // stress runs to be the drain-rate ceiling once
                    // foreground publish rate matches it. 1000/batch
                    // gives ~5-10x headroom for sustained-burst drain
                    // without changing the steady-state cost.
                    bo.MessageDeliveryLimit = 1000;

                    // Per-batch wall-clock budget for the publish loop.
                    // Larger batch needs proportionally more time to
                    // round-trip through Rabbit; 30s is generous enough
                    // that even a slow broker doesn't time out mid-batch.
                    bo.MessageDeliveryTimeout = TimeSpan.FromSeconds(30);
                });

                // QueryDelay is the IDLE poll cadence — when there's
                // nothing to deliver. Active sends are woken immediately
                // by BusOutboxNotification, so this only sets the upper
                // bound on post-restart delivery latency for messages
                // left in the outbox by a previous process.
                o.QueryDelay = TimeSpan.FromSeconds(60);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

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

    // Applies messaging DbContext migrations. Call once at startup, after
    // the other modules' Use*ModuleAsync calls. Same pattern as every
    // module's UseXModuleAsync.
    public static async Task UsePamMessagingAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PamMessagingDbContext>();
        await db.Database.MigrateAsync();
    }
}
