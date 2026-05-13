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
// follow-up PRs. Schema `wallet`, snake_case, audit columns. Outbox
// publishes (when the first feature ships) route through the shared
// PamMessagingDbContext (Pam.Shared.Messaging) — no ConfigureOutbox
// hook here, same as the other modules.
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
                options.UseSqlServer(
                    connectionString,
                    sql =>
                    {
                        sql.MigrationsHistoryTable("__EFMigrationsHistory", WalletDbContext.Schema);
                        sql.MigrationsAssembly(typeof(WalletDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        services
            .AddHealthChecks()
            .AddSqlServer(connectionString, name: "wallet-db", tags: ["ready", "module:wallet"]);

        return services;
    }

    public static async Task UseWalletModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        await db.Database.MigrateAsync();
    }
}
