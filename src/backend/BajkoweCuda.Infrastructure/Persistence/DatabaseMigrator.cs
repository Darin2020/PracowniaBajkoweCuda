using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Infrastructure.Persistence;

/// <summary>
/// Brings the database schema up to date, then makes sure the application's managed identity has a database user
/// with read and write access (and nothing more). Runs as the <c>migrate</c> command, in Azure as a Container Apps
/// job whose identity is a member of the SQL administrators group; the application itself cannot change the schema.
/// </summary>
public sealed partial class DatabaseMigrator(AppDbContext db, IOptions<DatabaseOptions> options, ILogger<DatabaseMigrator> logger)
{
    private static readonly string[] AppUserRoles = ["db_datareader", "db_datawriter"];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var pendingNames = string.Join(", ", pending);
        LogPendingMigrations(logger, pending.Count, pendingNames);

        await db.Database.MigrateAsync(cancellationToken);

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.AppUserName) && string.IsNullOrWhiteSpace(settings.AppUserClientId))
        {
            LogNoAppUser(logger);
            return;
        }

        var (name, clientId) = Validate(settings);
        await EnsureAppUserAsync(name, clientId, cancellationToken);
        LogAppUserReady(logger, name);
    }

    /// <summary>
    /// Both values end up in DDL statements, which cannot take parameters: only plain identity names and GUIDs are
    /// accepted, so nothing can break out of the bracketed identifier.
    /// </summary>
    internal static (string Name, Guid ClientId) Validate(DatabaseOptions settings)
    {
        if (settings.AppUserName is not { } name || !IdentityName().IsMatch(name))
        {
            throw new InvalidOperationException(
                "Database:AppUserName must be the managed identity name (letters, digits, '-' and '_', at most 128 characters).");
        }

        if (!Guid.TryParse(settings.AppUserClientId, out var clientId))
        {
            throw new InvalidOperationException("Database:AppUserClientId must be the client id (a GUID) of the managed identity.");
        }

        return (name, clientId);
    }

    /// <summary>
    /// <c>CREATE USER ... FROM EXTERNAL PROVIDER</c> would need Microsoft Graph permissions on the SQL server when it
    /// is run by a managed identity. Creating the user from the client id (<c>WITH SID ..., TYPE = E</c>) does not:
    /// the SID of an Entra application is its client id.
    /// </summary>
    private async Task EnsureAppUserAsync(string name, Guid clientId, CancellationToken cancellationToken)
    {
        // DDL cannot take parameters, so the statements are built here from the validated name (letters, digits,
        // '-' and '_' only) and run through sp_executesql; the existence checks do use parameters.
        await db.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
                EXEC sys.sp_executesql @statement;
            """,
            [Parameter("@name", name), Parameter("@statement", CreateUserStatement(name, clientId))],
            cancellationToken);

        foreach (var role in AppUserRoles)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                IF IS_ROLEMEMBER(@role, @name) = 0
                    EXEC sys.sp_executesql @statement;
                """,
                [Parameter("@role", role), Parameter("@name", name), Parameter("@statement", AddRoleMemberStatement(role, name))],
                cancellationToken);
        }
    }

    /// <summary>
    /// <c>CREATE USER [name] WITH SID = 0x..., TYPE = E</c> (Azure SQL only). The SID is the client id in the byte
    /// order SQL Server uses for <c>uniqueidentifier</c>, which is the order of <see cref="Guid.ToByteArray()"/>.
    /// </summary>
    internal static string CreateUserStatement(string name, Guid clientId) =>
        $"CREATE USER [{name}] WITH SID = 0x{Convert.ToHexString(clientId.ToByteArray())}, TYPE = E";

    internal static string AddRoleMemberStatement(string role, string name) => $"ALTER ROLE [{role}] ADD MEMBER [{name}]";

    private static Microsoft.Data.SqlClient.SqlParameter Parameter(string name, string value) =>
        new(name, System.Data.SqlDbType.NVarChar, -1) { Value = value };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex IdentityName();

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} pending migrations: {Migrations}")]
    private static partial void LogPendingMigrations(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "No application database user configured (Database:AppUserName); skipped")]
    private static partial void LogNoAppUser(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database user {Name} has read and write access")]
    private static partial void LogAppUserReady(ILogger logger, string name);
}
