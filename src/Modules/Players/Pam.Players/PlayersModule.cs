using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pam.Players.Data;
using Pam.Players.Infrastructure.Zitadel;
using Pam.Players.Players.Brands;
using Pam.Players.Players.Identity;
using Pam.Shared.Data.Interceptors;

namespace Pam.Players;

public static class PlayersModule
{
    public static IServiceCollection AddPlayersModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddScoped<AuditableSaveChangesInterceptor>();
        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<PlayersDbContext>(
            (sp, opts) =>
            {
                opts.UseNpgsql(
                    configuration.GetConnectionString("Pam"),
                    npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "player")
                );
                opts.AddInterceptors(
                    sp.GetRequiredService<AuditableSaveChangesInterceptor>(),
                    sp.GetRequiredService<DispatchDomainEventsInterceptor>()
                );
            }
        );

        services
            .AddOptions<BrandRegistryOptions>()
            .Bind(configuration.GetSection(BrandRegistryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IBrandRegistry, BrandRegistry>();

        services
            .AddOptions<ZitadelOptions>()
            .Bind(configuration.GetSection(ZitadelOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<ZitadelRuntimeState>();
        services.AddTransient<ZitadelTokenHandler>();

        services.AddHttpClient("zitadel-bootstrap");
        services.AddHttpClient("zitadel-readiness");

        services
            .AddHttpClient<IIdentityProvider, ZitadelIdentityProvider>(
                (sp, http) =>
                {
                    var opts = sp.GetRequiredService<IOptions<ZitadelOptions>>().Value;
                    http.BaseAddress = new Uri(opts.ManagementApiBaseUrl.TrimEnd('/') + "/");
                }
            )
            .AddHttpMessageHandler<ZitadelTokenHandler>();

        services.AddHostedService<ZitadelBootstrapService>();

        services
            .AddHealthChecks()
            .AddNpgSql(
                connectionString: configuration.GetConnectionString("Pam")
                    ?? throw new InvalidOperationException(
                        "ConnectionStrings:Pam is not configured"
                    ),
                name: "player.postgres",
                tags: ["ready", "module:player"]
            );

        return services;
    }

    public static IApplicationBuilder UsePlayersModule(this IApplicationBuilder app)
    {
        return app;
    }
}
