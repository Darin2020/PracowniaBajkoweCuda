using BajkoweCuda.Api.Observability;
using BajkoweCuda.Infrastructure.Accounts;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace BajkoweCuda.Api.Database;

/// <summary>
/// <c>dotnet BajkoweCuda.Api.dll migrate</c>: applies the EF Core migrations and exits. The same image that runs the
/// application runs this command, in Azure as a Container Apps job with its own identity (docs/RUNBOOK.md).
/// It needs the connection string and <c>Admin:Emails</c>, not the web settings of the application: after the
/// migrations it brings the Admin role in line with <c>Admin:Emails</c> (docs/PLAN.md §3.3).
/// </summary>
internal static partial class MigrateCommand
{
    public const string Name = "migrate";

    public static bool IsRequested(string[] args) => args is [Name, ..];

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args[1..]);
        builder.AddObservability();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>();
        builder.Services.AddAccounts(builder.Configuration);

        // Disposing the host flushes the buffered logs and telemetry before the job container stops.
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrateCommand));

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().RunAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<AdminRoleSync>().SyncAllAsync(CancellationToken.None);
            LogFinished(logger);
            return 0;
        }
#pragma warning disable CA1031 // The process exit code is the result: any failure must end as a non-zero exit, logged.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migration finished")]
    private static partial void LogFinished(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Database migration failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
