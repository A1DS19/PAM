using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pam.Shared.Data.Interceptors;
using Pam.Wallet.Data;

namespace Pam.Wallet;

// Module #4. Scaffold-only — Account aggregate exists, real
// double-entry ledger (LedgerEntry / Transaction with sum-to-zero
// invariants, balance snapshots, multi-currency conversion) lands in
// follow-up PRs. What's set up on day one:
//
// - WalletDbContext with the MassTransit outbox model entities
//   (inbox_state / outbox_state / outbox_message). The migration
//   provisions the tables so the moment ConfigureOutbox below is
//   passed to AddPamMassTransit, publishes from a SaveChanges scope
//   commit atomically with the row write.
//
// - schema `wallet`, snake_case, audit columns. Same pattern as every
//   other module.
public static class WalletModule
{
    public static IServiceCollection AddWalletModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration.GetConnectionString("Pam")
            ?? throw new InvalidOperationException("ConnectionStrings:Pam is not configured");

        services.TryAddScoped<AuditableSaveChangesInterceptor>();
        services.TryAddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<WalletDbContext>(
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
                        npg.MigrationsHistoryTable("__EFMigrationsHistory", WalletDbContext.Schema);
                        npg.MigrationsAssembly(typeof(WalletDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        services
            .AddHealthChecks()
            .AddNpgSql(connectionString, name: "wallet-db", tags: ["ready", "module:wallet"]);

        return services;
    }

    // Bus-side outbox config, mirroring OperatorsModule.ConfigureOutbox
    // (which lives on chore/operators-outbox). Wired up in Program.cs by
    // passing this delegate to AddPamMassTransit alongside the other
    // module configurators. No-op until AddPamMassTransit gains the
    // configureBus parameter from the outbox infrastructure PR.
    public static void ConfigureOutbox(IBusRegistrationConfigurator bus)
    {
        bus.AddEntityFrameworkOutbox<WalletDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
            o.QueryDelay = TimeSpan.FromSeconds(1);
            o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
        });
    }

    public static async Task UseWalletModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        await db.Database.MigrateAsync();
    }
}
