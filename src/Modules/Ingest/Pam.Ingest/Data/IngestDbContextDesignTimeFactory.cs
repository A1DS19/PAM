using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pam.Ingest.Data;

public sealed class IngestDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IngestDbContext>
{
    public IngestDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("PAM_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pam;Username=pam;Password=pam_dev_password";

        var options = new DbContextOptionsBuilder<IngestDbContext>()
            .UseNpgsql(
                connection,
                npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", IngestDbContext.Schema)
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IngestDbContext(options);
    }
}
