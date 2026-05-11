using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pam.Players.Data;

public sealed class PlayersDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<PlayersDbContext>
{
    public PlayersDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("PAM_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pam;Username=pam;Password=pam_dev_password";

        var options = new DbContextOptionsBuilder<PlayersDbContext>()
            .UseNpgsql(
                connection,
                npg =>
                    npg.MigrationsHistoryTable("__EFMigrationsHistory", PlayersDbContext.Schema)
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PlayersDbContext(options);
    }
}
