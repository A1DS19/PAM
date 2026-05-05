using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pam.Players.Data;
using Pam.Players.Infrastructure.Keycloak;
using Pam.Players.Players.Identity;
using Pam.Shared.Data.Interceptors;

namespace Pam.Players;

public static class PlayersModule
{
    public static IServiceCollection AddPlayersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<AuditableSaveChangesInterceptor>();
        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<PlayersDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(
                configuration.GetConnectionString("Pam"),
                npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "player"));
            opts.AddInterceptors(
                sp.GetRequiredService<AuditableSaveChangesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>());
        });

        services
            .AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SectionName))
            .ValidateOnStart();

        services.AddTransient<AdminTokenHandler>();
        services.AddHttpClient("keycloak-token");

        services
            .AddHttpClient<IIdentityProvider, KeycloakIdentityProvider>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<KeycloakOptions>>().Value;
                http.BaseAddress = new Uri(opts.AuthServerUrl.TrimEnd('/') + "/");
            })
            .AddHttpMessageHandler<AdminTokenHandler>();

        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: configuration.GetConnectionString("Pam")
                    ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured"),
                name: "player.postgres",
                tags: ["ready", "module:player"]);

        return services;
    }

    public static IApplicationBuilder UsePlayersModule(this IApplicationBuilder app)
    {
        return app;
    }
}
