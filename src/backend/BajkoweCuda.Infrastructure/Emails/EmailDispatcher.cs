using System.Text.Json;
using BajkoweCuda.Application.Emails;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Infrastructure.Emails;

/// <summary>
/// Sends the emails of the outbox (docs/PLAN.md §3.6), with exponential retries and a dead-letter state.
/// It reads the database once at startup, when an email is queued (<see cref="EmailDispatchSignal"/>) and when the
/// next delayed email or retry is due, never on a fixed timer, so an idle site lets the serverless database pause.
/// </summary>
public sealed partial class EmailDispatcher(
    IServiceScopeFactory scopes,
    EmailDispatchSignal signal,
    IOptions<EmailOptions> options,
    TimeProvider time,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan Idle = TimeSpan.FromDays(1);
    private static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                var nextDue = await DispatchDueAsync(stoppingToken);
                wait = nextDue is null ? Idle : Clamp(nextDue.Value - time.GetUtcNow(), TimeSpan.Zero, Idle);
            }
#pragma warning disable CA1031 // The worker must survive any failure (for example a paused database) and try again.
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogDispatchFailed(logger, exception);
                wait = AfterFailure;
            }

            await signal.WaitAsync(wait, time, stoppingToken);
        }
    }

    /// <summary>Sends every due email; returns when the next pending one is due, or <c>null</c> if none is pending.</summary>
    public async Task<DateTimeOffset?> DispatchDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        while (true)
        {
            var now = time.GetUtcNow();
            var due = await db.EmailOutbox
                .Where(message => message.Status == EmailStatus.Pending && message.SendAfter <= now)
                .OrderBy(message => message.SendAfter)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            foreach (var message in due)
            {
                await SendAsync(message, sender, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            if (due.Count < BatchSize)
            {
                break;
            }
        }

        return await db.EmailOutbox
            .Where(message => message.Status == EmailStatus.Pending)
            .OrderBy(message => message.SendAfter)
            .Select(message => (DateTimeOffset?)message.SendAfter)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task SendAsync(EmailOutboxMessage message, IEmailSender sender, CancellationToken cancellationToken)
    {
        try
        {
            var template = Enum.Parse<EmailTemplate>(message.Template);
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(message.PayloadJson) ?? [];
            await sender.SendAsync(EmailRenderer.Render(message.ToEmail, template, message.Language, payload), cancellationToken);

            message.Status = EmailStatus.Sent;
            message.SentAt = time.GetUtcNow();
            message.LastError = null;
            var recipient = EmailAddresses.Mask(message.ToEmail);
            LogSent(logger, message.Template, recipient);
        }
#pragma warning disable CA1031 // Any failure of one email is recorded on that email; the others are still sent.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            message.Attempts++;
            message.LastError = Truncate(EmailAddresses.MaskAll($"{exception.GetType().Name}: {exception.Message}"));

            if (message.Attempts >= options.Value.MaxAttempts)
            {
                message.Status = EmailStatus.Dead;
                LogDead(logger, message.Template, message.Id, message.Attempts, message.LastError);
            }
            else
            {
                message.SendAfter = time.GetUtcNow() + RetryDelay(message.Attempts);
                LogRetry(logger, message.Template, message.Id, message.Attempts, message.SendAfter, message.LastError);
            }
        }
    }

    /// <summary>30 s, 1 min, 2 min, 4 min ... at most 2 hours.</summary>
    internal static TimeSpan RetryDelay(int attempts) =>
        Clamp(FirstRetry * Math.Pow(2, attempts - 1), FirstRetry, LongestRetry);

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    private static string Truncate(string value) =>
        value.Length <= EmailOutboxMessage.LastErrorMaxLength ? value : value[..EmailOutboxMessage.LastErrorMaxLength];

    // The exception itself is not logged: mail server messages can contain the recipient's address.
    // Only its masked text (LastError) is.
    [LoggerMessage(Level = LogLevel.Information, Message = "Sent {Template} email to {Recipient}")]
    private static partial void LogSent(ILogger logger, string template, string recipient);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sending {Template} email {MessageId} failed (attempt {Attempts}), next try at {SendAfter}: {Error}")]
    private static partial void LogRetry(ILogger logger, string template, Guid messageId, int attempts, DateTimeOffset sendAfter, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up sending {Template} email {MessageId} after {Attempts} attempts: {Error}")]
    private static partial void LogDead(ILogger logger, string template, Guid messageId, int attempts, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email dispatch failed; retrying in a minute")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);
}
