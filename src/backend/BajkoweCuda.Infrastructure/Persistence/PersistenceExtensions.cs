using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public const string ConnectionStringName = "Sql";

    /// <summary>
    /// Registers the database. The connection string is required: a deployment without one must fail at startup.
    /// In Azure it carries no secret (<c>Authentication=Active Directory Managed Identity</c>).
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The connection string 'ConnectionStrings:{ConnectionStringName}' is not configured.");
        }

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString, ConfigureSqlServer));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddScoped<DatabaseMigrator>();

        return services;
    }

    internal static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sql)
    {
        sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        // Azure SQL drops connections now and then, and the serverless database answers slowly while it resumes.
        sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
    }
}
