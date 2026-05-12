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

        // No outbox entities here — see OperatorsDbContext.OnModelCreating
        // for the rationale. The MT outbox lives in
        // PamMessagingDbContext (schema "messaging").
    }
}
