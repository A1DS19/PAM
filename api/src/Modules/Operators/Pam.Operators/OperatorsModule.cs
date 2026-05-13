using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pam.Operators.Data;
using Pam.Shared.Data.Interceptors;

namespace Pam.Operators;

public static class OperatorsModule
{
    // No ConfigureOutbox here. The bus-wide outbox lives on
    // PamMessagingDbContext (Pam.Shared.Messaging), wired once in
    // AddPamMassTransit. BrandCreatedDomainHandler keeps calling
    // IPublishEndpoint.Publish — the publish is intercepted by the
    // bus-wide filter and the OutboxMessage row lands in the messaging
    // context, flushed at the tail of the command pipeline by
    // OutboxFlushBehavior.

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
                options.UseSqlServer(
                    connectionString,
                    sql =>
                    {
                        sql.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            OperatorsDbContext.Schema
                        );
                        sql.MigrationsAssembly(typeof(OperatorsDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        services
            .AddHealthChecks()
            .AddSqlServer(connectionString, name: "operators-db", tags: ["ready", "module:operators"]);

        return services;
    }

    public static async Task UseOperatorsModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperatorsDbContext>();
        await db.Database.MigrateAsync();
    }
}
