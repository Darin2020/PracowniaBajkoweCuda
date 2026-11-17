using BajkoweCuda.Api.Observability;
using BajkoweCuda.Infrastructure.Persistence;

namespace BajkoweCuda.Api.Database;

/// <summary>
/// <c>dotnet BajkoweCuda.Api.dll migrate</c>: applies the EF Core migrations and exits. The same image that runs the
/// application runs this command, in Azure as a Container Apps job with its own identity (docs/RUNBOOK.md).
/// It needs only the connection string, not the web settings of the application.
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

        // Disposing the host flushes the buffered logs and telemetry before the job container stops.
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrateCommand));

        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().RunAsync(CancellationToken.None);
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
