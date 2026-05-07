using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pam.Operators.Data;
using Pam.Shared.Data.Interceptors;

namespace Pam.Operators;

public static class OperatorsModule
{
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
