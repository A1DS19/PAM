using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pam.Identity.Data;

// Design-time factory used by `dotnet ef migrations add` etc. The runtime
// connection string comes from configuration (ConnectionStrings:Pam) via
// IdentityModule.AddIdentityModule; design-time tooling can't read that, so
// it falls back to PAM_DESIGN_CONNECTION or the dev defaults.
public sealed class IdentityDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("PAM_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=pam;Username=pam;Password=pam_dev_password";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                connection,
                npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)
            )
            .UseSnakeCaseNamingConvention()
            .UseOpenIddict()
            .Options;

        return new IdentityDbContext(options);
    }
}
