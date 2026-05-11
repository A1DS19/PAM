using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pam.Audit.Data;
using Pam.Audit.Services;
using Pam.Shared.Contracts.Audit;

namespace Pam.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Pam")
            ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured");

        services.AddDbContext<AuditDbContext>(
            (_, options) =>
            {
                options.UseNpgsql(
                    connectionString,
                    npg =>
                    {
                        npg.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema);
                        npg.MigrationsAssembly(typeof(AuditDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        services.AddScoped<IAuditService, AuditService>();

        services
            .AddHealthChecks()
            .AddNpgSql(connectionString, name: "audit-db", tags: ["ready", "module:audit"]);

        return services;
    }

    public static async Task UseAuditModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await db.Database.MigrateAsync();
    }
}
