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

    // Bus-side outbox config. Per-DbContext delivery service polls
    // wallet.outbox_state and forwards messages to RabbitMQ.
    //
    // IMPORTANT — do NOT call .UseBusOutbox() here. That method installs
    // the bus-level publish-intercepting filter as a singleton
    // (IBusOutboxNotification). Calling it more than once across module
    // configurators replaces the singleton with the second registration,
    // leaving the first delivery service holding a stale reference and
    // throwing NullReferenceException on every poll.
    //
    // OperatorsModule.ConfigureOutbox owns the single UseBusOutbox()
    // call. The filter it installs is bus-wide — it intercepts publishes
    // from ANY DbContext that has AddEntityFrameworkOutbox<T> registered,
    // including this one. Future publishing modules follow the same rule.
    public static void ConfigureOutbox(IBusRegistrationConfigurator bus)
    {
        bus.AddEntityFrameworkOutbox<WalletDbContext>(o =>
        {
            o.UsePostgres();
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
