using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pam.Ingest.Data;
using Pam.Ingest.Vendors.TwentyOneG;
using Pam.Shared.Data.Interceptors;

namespace Pam.Ingest;

public static class IngestModule
{
    // Outbox config — same per-DbContext pattern as OperatorsModule and
    // WalletModule. We do NOT call UseBusOutbox here; OperatorsModule
    // owns that single registration across the bus. See the comment in
    // OperatorsModule.ConfigureOutbox for why.
    public static void ConfigureOutbox(IBusRegistrationConfigurator bus)
    {
        bus.AddEntityFrameworkOutbox<IngestDbContext>(o =>
        {
            o.UsePostgres();
            o.QueryDelay = TimeSpan.FromSeconds(60);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
        });
    }

    public static IServiceCollection AddIngestModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Pam")
            ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured");

        services.TryAddScoped<AuditableSaveChangesInterceptor>();
        services.TryAddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<IngestDbContext>(
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
                        npg.MigrationsHistoryTable("__EFMigrationsHistory", IngestDbContext.Schema);
                        npg.MigrationsAssembly(typeof(IngestDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        // Vendor adapters — one per casino vendor. Add new adapters here
        // when wiring a new vendor. Scoped because the adapter may resolve
        // scoped services (IPlayerLookup once Players ships).
        services.AddScoped<TwentyOneGAdapter>();

        services
            .AddHealthChecks()
            .AddNpgSql(connectionString, name: "ingest-db", tags: ["ready", "module:ingest"]);

        return services;
    }

    public static async Task UseIngestModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        await db.Database.MigrateAsync();
    }
}
