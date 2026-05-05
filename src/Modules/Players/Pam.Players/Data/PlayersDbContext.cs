using Microsoft.EntityFrameworkCore;
using Pam.Players.Players.Models;

namespace Pam.Players.Data;

public sealed class PlayersDbContext(DbContextOptions<PlayersDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("player");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlayersDbContext).Assembly);
    }
}
