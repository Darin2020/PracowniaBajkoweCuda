using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BajkoweCuda.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>. Adding a migration needs the model, not a database,
/// so the connection string is a placeholder and no configuration or secrets are read.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=design-time;Database=BajkoweCuda", PersistenceExtensions.ConfigureSqlServer)
            .Options;

        return new AppDbContext(options);
    }
}
