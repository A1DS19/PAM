using Microsoft.EntityFrameworkCore;
using Pam.Players.Players.Models;

namespace Pam.Players.Data;

public sealed class PlayersDbContext(DbContextOptions<PlayersDbContext> options) : DbContext(options)
{
    public const string Schema = "players";

    public DbSet<Player> Players => Set<Player>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlayersDbContext).Assembly);

        // Brand-scoped global query filter goes here once authenticated
        // player endpoints land. Pattern:
        //
        //   modelBuilder.Entity<Player>()
        //       .HasQueryFilter(p => p.BrandId == _brandContext.CurrentBrandId);
        //
        // Requires an injected IBrandContext (read from the JWT
        // `brand_id` claim via HttpUserContext-style lookup). Operator
        // endpoints elevate to cross-brand via the
        // `operator.platform-admin` permission; see ARCHITECTURE.md.
    }
}
