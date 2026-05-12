using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pam.Ingest.Data;

public sealed class IngestDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IngestDbContext>
{
    public IngestDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("PAM_DESIGN_CONNECTION")
            ?? "Server=localhost,1433;Database=pam;User Id=sa;Password=Pam_dev_password_123!;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<IngestDbContext>()
            .UseSqlServer(
                connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IngestDbContext.Schema)
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IngestDbContext(options);
    }
}
