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
            ?? "Server=localhost,1433;Database=pam;User Id=sa;Password=Pam_dev_password_123!;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseSqlServer(
                connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WalletDbContext.Schema)
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new WalletDbContext(options);
    }
}
