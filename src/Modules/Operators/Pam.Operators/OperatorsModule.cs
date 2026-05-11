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
    public static void ConfigureOutbox(IBusRegistrationConfigurator bus)
    {
        bus.AddEntityFrameworkOutbox<OperatorsDbContext>(o =>
        {
            o.UsePostgres();

            // UseBusOutbox enables transactional publish: IPublishEndpoint
            // calls from a SaveChanges scope write to OutboxMessage in the
            // same transaction instead of going straight to the broker.
            o.UseBusOutbox();

            o.QueryDelay = TimeSpan.FromSeconds(1);
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
            .AddNpgSql(
                connectionString,
                name: "operators-db",
                tags: ["ready", "module:operators"]
            );

        return services;
    }

    public static async Task UseOperatorsModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperatorsDbContext>();
        await db.Database.MigrateAsync();
    }
}
