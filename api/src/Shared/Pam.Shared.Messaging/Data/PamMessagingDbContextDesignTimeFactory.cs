using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pam.Shared.Messaging.Data;

// Design-time factory used by `dotnet ef migrations add` etc. Same shape as
// every module's design-time factory — falls back to PAM_DESIGN_CONNECTION
// or the local dev connection string.
public sealed class PamMessagingDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<PamMessagingDbContext>
{
    public PamMessagingDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("PAM_DESIGN_CONNECTION")
            ?? "Server=localhost,1433;Database=pam;User Id=sa;Password=Pam_dev_password_123!;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<PamMessagingDbContext>()
            .UseSqlServer(
                connection,
                sql =>
                    sql.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        PamMessagingDbContext.Schema
                    )
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PamMessagingDbContext(options);
    }
}
