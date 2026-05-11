using MassTransit;
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

        // MassTransit outbox tables — integration-event publishes from inside
        // a SaveChanges scope (BrandCreatedDomainHandler etc.) write to
        // OutboxMessage in the same transaction. A delivery service polls
        // and forwards to RabbitMQ. InboxState exists for the symmetric
        // case (deduplicating inbound consumes) and is harmless even if
        // this module never consumes events.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
