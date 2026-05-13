using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pam.Players.Data;
using Pam.Shared.Data.Interceptors;

namespace Pam.Players;

// Module #3. Scaffold-only: PlayersDbContext + Player aggregate exist;
// features (registration, KYC, sessions, limits) land in follow-up PRs
// per the per-module pattern in ARCHITECTURE.md. Player auth uses a
// distinct OpenIddict audience (`pam_player_api`) from the back-office
// audience — that wiring lands with the first authenticated endpoint.
public static class PlayersModule
{
    public static IServiceCollection AddPlayersModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Pam")
            ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured");

        services.TryAddScoped<AuditableSaveChangesInterceptor>();
        services.TryAddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<PlayersDbContext>(
            (sp, options) =>
            {
                options.AddInterceptors(
                    sp.GetRequiredService<AuditableSaveChangesInterceptor>(),
                    sp.GetRequiredService<DispatchDomainEventsInterceptor>(),
                    sp.GetRequiredService<AmbientTransactionInterceptor>()
                );
                options.UseSqlServer(
                    connectionString,
                    sql =>
                    {
                        sql.MigrationsHistoryTable("__EFMigrationsHistory", PlayersDbContext.Schema);
                        sql.MigrationsAssembly(typeof(PlayersDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        services
            .AddHealthChecks()
            .AddSqlServer(connectionString, name: "players-db", tags: ["ready", "module:players"]);

        return services;
    }

    public static async Task UsePlayersModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlayersDbContext>();
        await db.Database.MigrateAsync();
    }
}
