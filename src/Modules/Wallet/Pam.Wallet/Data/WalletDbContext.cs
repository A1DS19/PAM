using MassTransit;
using Microsoft.EntityFrameworkCore;
using Pam.Wallet.Accounts.Models;

namespace Pam.Wallet.Data;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options)
    : DbContext(options)
{
    public const string Schema = "wallet";

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);

        // Outbox model on day one — even before the first feature ships,
        // the migration provisions the inbox/outbox/outbox_message tables
        // so the bus-side wiring (WalletModule.ConfigureOutbox passed to
        // AddPamMassTransit in Program.cs) can light up the moment the
        // shared outbox infrastructure lands.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        // Brand-scoped global query filter goes here once authenticated
        // wallet endpoints exist (see PlayersDbContext for the same hook
        // location). Wallet rows MUST never leak across brands.
    }
}
