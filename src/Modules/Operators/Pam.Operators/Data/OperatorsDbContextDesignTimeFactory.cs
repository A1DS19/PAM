using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pam.Operators.Data;

// Design-time factory used by `dotnet ef migrations add` etc. The runtime
// connection string comes from configuration (ConnectionStrings:Pam) via
// OperatorsModule.AddOperatorsModule; design-time tooling can't read that,
// so it falls back to PAM_DESIGN_CONNECTION or the dev defaults.
public sealed class OperatorsDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<OperatorsDbContext>
{
    public OperatorsDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("PAM_DESIGN_CONNECTION")
            ?? "Server=localhost,1433;Database=pam;User Id=sa;Password=Pam_dev_password_123!;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<OperatorsDbContext>()
            .UseSqlServer(
                connection,
                sql =>
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", OperatorsDbContext.Schema)
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OperatorsDbContext(options);
    }
}
