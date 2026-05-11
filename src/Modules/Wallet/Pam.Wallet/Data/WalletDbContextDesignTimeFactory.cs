using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pam.Wallet.Data;

public sealed class WalletDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("PAM_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pam;Username=pam;Password=pam_dev_password";

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(
                connection,
                npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", WalletDbContext.Schema)
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new WalletDbContext(options);
    }
}
