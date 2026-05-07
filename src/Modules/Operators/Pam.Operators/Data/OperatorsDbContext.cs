using Microsoft.EntityFrameworkCore;
using Pam.Operators.Brands.Models;

namespace Pam.Operators.Data;

public sealed class OperatorsDbContext(DbContextOptions<OperatorsDbContext> options)
    : DbContext(options)
{
    public const string Schema = "operators";

    public DbSet<Brand> Brands => Set<Brand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OperatorsDbContext).Assembly);
    }
}
