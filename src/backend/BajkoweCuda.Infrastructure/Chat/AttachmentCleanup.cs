using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BajkoweCuda.Infrastructure.Chat;

/// <summary>Deletes chat uploads that were never sent (docs/PLAN.md §5), once an hour, in batches.</summary>
internal sealed partial class AttachmentCleanup(IServiceScopeFactory scopes, TimeProvider time, ILogger<AttachmentCleanup> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var attachments = scope.ServiceProvider.GetRequiredService<Attachments>();
                int deleted;
                var total = 0;
                do
                {
                    deleted = await attachments.DeleteAbandonedAsync(BatchSize, stoppingToken);
                    total += deleted;
                }
                while (deleted == BatchSize && !stoppingToken.IsCancellationRequested);

                if (total > 0)
                {
                    LogDeleted(logger, total);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // The database or storage may be away for a moment (serverless SQL resuming); try again next hour.
                LogFailed(logger, error);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} abandoned chat uploads")]
    private static partial void LogDeleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up abandoned chat uploads failed")]
    private static partial void LogFailed(ILogger logger, Exception error);
}
