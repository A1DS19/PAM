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

        // No outbox entities here — see OperatorsDbContext.OnModelCreating
        // for the rationale. The MT outbox lives in
        // PamMessagingDbContext (schema "messaging").

        // Brand-scoped global query filter goes here once authenticated
        // wallet endpoints exist (see PlayersDbContext for the same hook
        // location). Wallet rows MUST never leak across brands.
    }
}
