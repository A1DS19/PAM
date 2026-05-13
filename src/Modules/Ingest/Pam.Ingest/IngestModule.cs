using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pam.Ingest.Data;
using Pam.Ingest.Transactions.Reconciliation;
using Pam.Ingest.Vendors.TwentyOneG;
using Pam.Ingest.Vendors.TwentyOneG.Soap;
using Pam.Shared.Data.Interceptors;
using Pam.Shared.Messaging.Reconciliation;
using SoapCore;

namespace Pam.Ingest;

public static class IngestModule
{
    // No ConfigureOutbox here — see OperatorsModule for the architectural
    // note. TransactionIngestedDomainHandler keeps calling
    // IPublishEndpoint.Publish unchanged; the bus-wide outbox on
    // PamMessagingDbContext captures the row.

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
                options.UseSqlServer(
                    connectionString,
                    sql =>
                    {
                        sql.MigrationsHistoryTable("__EFMigrationsHistory", IngestDbContext.Schema);
                        sql.MigrationsAssembly(typeof(IngestDbContext).Assembly.FullName);
                    }
                );
                options.UseSnakeCaseNamingConvention();
            }
        );

        // Vendor adapters — one per casino vendor. Add new adapters here
        // when wiring a new vendor. Scoped because the adapter may resolve
        // scoped services (IPlayerLookup once Players ships).
        services.AddScoped<TwentyOneGAdapter>();

        // SoapCore — required once for the host to serve SOAP envelopes.
        // The per-endpoint mapping happens in UseIngestSoapEndpoints below.
        services.AddSoapCore();

        // 21G SOAP service implementations. One per endpoint; same WSDL
        // shape, different behavior. See infra/wsdl/21g/ for the source
        // of truth on the contract.
        services.AddScoped<
            ITwentyOneGCustomerTransactionService,
            TwentyOneGCustomerTransactionService
        >();
        services.AddScoped<ITwentyOneGValidateSessionService, TwentyOneGValidateSessionService>();
        services.AddScoped<ITwentyOneGGetBalanceService, TwentyOneGGetBalanceService>();

        // Reconciler backstop. OutboxReconciliationService (registered in
        // AddPamMassTransit) iterates every IOutboxReconciler every
        // Messaging:Reconciliation:Interval and asks each module to
        // republish business rows whose dispatched-log entry is missing.
        // Scoped because the reconciler depends on scoped DbContexts +
        // IPublishEndpoint.
        services.AddScoped<IOutboxReconciler, IngestOutboxReconciler>();

        services
            .AddHealthChecks()
            .AddSqlServer(connectionString, name: "ingest-db", tags: ["ready", "module:ingest"]);

        return services;
    }

    public static async Task UseIngestModuleAsync(this IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();
        await db.Database.MigrateAsync();
    }

    // Maps SoapCore middleware at the same URL paths GBS uses today, so
    // routing 21G to PAM is a host-portion swap with no path changes.
    // MUST be mounted BEFORE UseAuthentication() in Program.cs — vendor
    // traffic doesn't carry a PAM JWT, and the fallback authorization
    // policy would 401 it. SoapCore middleware short-circuits on path
    // match; non-SOAP requests fall through to the rest of the pipeline.
    public static IApplicationBuilder UseIngestSoapEndpoints(this IApplicationBuilder app)
    {
        var soapOptions = new SoapEncoderOptions();

        app.UseSoapEndpoint<ITwentyOneGCustomerTransactionService>(
            TwentyOneGSoapDefaults.CustomerTransactionPath,
            soapOptions,
            SoapSerializer.XmlSerializer
        );

        app.UseSoapEndpoint<ITwentyOneGValidateSessionService>(
            TwentyOneGSoapDefaults.ValidateSessionPath,
            soapOptions,
            SoapSerializer.XmlSerializer
        );

        app.UseSoapEndpoint<ITwentyOneGGetBalanceService>(
            TwentyOneGSoapDefaults.GetBalancePath,
            soapOptions,
            SoapSerializer.XmlSerializer
        );

        return app;
    }
}
