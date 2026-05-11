using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pam.Operators.Data;
using Pam.Shared.Data.Interceptors;

namespace Pam.Operators;

public static class OperatorsModule
{
    // Outbox config for the bus — passed to AddPamMassTransit from
    // Program.cs. Lives here so the module owns the choice of polling
    // interval, delivery batch size, etc. as those tune over time.
    //
    // This module owns the single .UseBusOutbox() call across the whole
    // bus. UseBusOutbox installs the publish-intercepting filter as a
    // singleton; calling it from more than one module configurator
    // replaces the singleton and trips NullReferenceException in the
    // BusOutboxDeliveryService on every poll. Other modules
    // (WalletModule.ConfigureOutbox, future publishers) register their
    // AddEntityFrameworkOutbox<T> per-DbContext but omit UseBusOutbox.
    // The filter this call installs intercepts publishes from ANY of
    // those DbContexts.
    public static void ConfigureOutbox(IBusRegistrationConfigurator bus)
    {
        bus.AddEntityFrameworkOutbox<OperatorsDbContext>(o =>
        {
            o.UsePostgres();

            // UseBusOutbox enables transactional publish: IPublishEndpoint
            // calls from a SaveChanges scope write to OutboxMessage in the
            // same transaction instead of going straight to the broker.
            // See note above — this is the single call across the bus.
            o.UseBusOutbox();

            // QueryDelay is the IDLE poll cadence — when there's nothing
            // to deliver. Active sends are woken immediately by
            // BusOutboxNotification, so this only sets the upper bound on
            // post-restart delivery latency for messages left in the
            // outbox by a previous process. 60s keeps the dev log quiet
            // and the DB workload negligible; tune down for production
            // restart-sensitivity.
            o.QueryDelay = TimeSpan.FromSeconds(60);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
        });
    }

    public static IServiceCollection AddOperatorsModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Pam")
            ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured");

        services.TryAddScoped<AuditableSaveChangesInterceptor>();
        services.TryAddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<OperatorsDbContext>(
            (sp, options) =>
            {
                options.AddInterceptors(
                    sp.GetRequiredService<AuditableSaveChangesInterceptor>(),
                    sp.GetRequiredService<DispatchDomainEventsInterceptor>()
                );
                options.UseNpgsql(
                    connectionString,
                    npg =>
                    {
                        npg.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            OperatorsDbContext.Schema
                        );
                        npg.MigrationsAssembly(typeof(OperatorsDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        services
            .AddHealthChecks()
            .AddNpgSql(connectionString, name: "operators-db", tags: ["ready", "module:operators"]);

        return services;
    }

    public static async Task UseOperatorsModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperatorsDbContext>();
        await db.Database.MigrateAsync();
    }
}
