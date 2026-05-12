using Microsoft.EntityFrameworkCore;
using Pam.Operators.Brands.Models;

namespace Pam.Operators.Data;

public sealed class OperatorsDbContext(DbContextOptions<OperatorsDbContext> options)
    : DbContext(options)
{
    public const string Schema = "operators";

    public DbSet<Brand> Brands => Set<Brand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OperatorsDbContext).Assembly);

        // No outbox entities here. PAM hosts MassTransit's inbox/outbox
        // tables in a single shared messaging schema owned by
        // PamMessagingDbContext (Pam.Shared.Messaging). Bridge handlers
        // call IPublishEndpoint.Publish as before — the bus-wide
        // UseBusOutbox in AddPamMassTransit routes the OutboxMessage row
        // into the messaging context, which OutboxFlushBehavior commits
        // at the tail of each command pipeline.
    }
}
