using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pam.Players.Data;

public sealed class PlayersDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PlayersDbContext>
{
    public PlayersDbContext CreateDbContext(string[] args)
    {
        var conn =
            Environment.GetEnvironmentVariable("PAM_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pam;Username=pam;Password=pam_dev_password";
        var opts = new DbContextOptionsBuilder<PlayersDbContext>()
            .UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "player"))
            .Options;
        return new PlayersDbContext(opts);
    }
}
