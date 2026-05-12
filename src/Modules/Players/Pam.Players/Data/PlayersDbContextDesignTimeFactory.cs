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
            ?? "Server=localhost,1433;Database=pam;User Id=sa;Password=Pam_dev_password_123!;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<PlayersDbContext>()
            .UseSqlServer(
                connection,
                sql =>
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", PlayersDbContext.Schema)
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PlayersDbContext(options);
    }
}
