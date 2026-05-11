using MassTransit;
using Microsoft.EntityFrameworkCore;
using Pam.Ingest.Transactions.Models;

namespace Pam.Ingest.Data;

public sealed class IngestDbContext(DbContextOptions<IngestDbContext> options) : DbContext(options)
{
    public const string Schema = "ingest";

    public DbSet<VendorTransaction> VendorTransactions => Set<VendorTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IngestDbContext).Assembly);

        // MassTransit outbox tables — TransactionIngestedDomainHandler's
        // IPublishEndpoint.Publish call writes to OutboxMessage in the
        // same transaction as the VendorTransaction row. See
        // ARCHITECTURE.md "Outbox + pre-save domain-event dispatch".
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
